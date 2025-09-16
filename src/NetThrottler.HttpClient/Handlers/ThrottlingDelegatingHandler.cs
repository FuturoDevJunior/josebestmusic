using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NetThrottler.Core.Interfaces;
using NetThrottler.Core.Policies;
using NetThrottler.HttpClient.Options;
using System.Net;

namespace NetThrottler.HttpClient.Handlers;

/// <summary>
/// A delegating handler that applies rate limiting to HTTP requests.
/// </summary>
public class ThrottlingDelegatingHandler : DelegatingHandler
{
    private readonly IThrottleStorage _storage;
    private readonly HttpClientThrottlingOptions _options;
    private readonly ILogger<ThrottlingDelegatingHandler> _logger;
    private readonly Dictionary<string, IRateLimiter> _rateLimiters = new();
    private readonly SemaphoreSlim _rateLimitersLock = new(1, 1);

    /// <summary>
    /// Initializes a new instance of the <see cref="ThrottlingDelegatingHandler"/> class.
    /// </summary>
    /// <param name="storage">The throttle storage instance.</param>
    /// <param name="options">The throttling options.</param>
    /// <param name="logger">The logger instance.</param>
    public ThrottlingDelegatingHandler(
        IThrottleStorage storage,
        IOptions<HttpClientThrottlingOptions> options,
        ILogger<ThrottlingDelegatingHandler> logger)
    {
        _storage = storage ?? throw new ArgumentNullException(nameof(storage));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Sends an HTTP request with rate limiting applied.
    /// </summary>
    /// <param name="request">The HTTP request message.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The HTTP response message.</returns>
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        if (request == null)
            throw new ArgumentNullException(nameof(request));

        var key = ResolveThrottlingKey(request);
        var policy = await GetOrCreateRateLimiterAsync(key, cancellationToken);

        var attempt = 0;
        var maxAttempts = _options.MaxRetryAttempts + 1;

        while (attempt < maxAttempts)
        {
            attempt++;

            try
            {
                var isAllowed = await policy.TryAcquireAsync(key, 1, cancellationToken);

                if (isAllowed)
                {
                    _logger.LogDebug("Request allowed for key: {Key}, attempt: {Attempt}", key, attempt);
                    return await base.SendAsync(request, cancellationToken);
                }

                _logger.LogWarning("Request rate limited for key: {Key}, attempt: {Attempt}", key, attempt);

                if (_options.ThrowOnRateLimit)
                {
                    throw new HttpRequestException($"Rate limit exceeded for key: {key}");
                }

                if (attempt < maxAttempts)
                {
                    var delay = CalculateRetryDelay(attempt);
                    _logger.LogDebug("Waiting {Delay}ms before retry {Attempt} for key: {Key}", delay.TotalMilliseconds, attempt + 1, key);
                    await Task.Delay(delay, cancellationToken);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex) when (attempt < maxAttempts)
            {
                _logger.LogWarning(ex, "Request failed for key: {Key}, attempt: {Attempt}, retrying", key, attempt);
                var delay = CalculateRetryDelay(attempt);
                await Task.Delay(delay, cancellationToken);
            }
        }

        throw new HttpRequestException($"Request failed after {maxAttempts} attempts for key: {key}");
    }

    private string ResolveThrottlingKey(HttpRequestMessage request)
    {
        return _options.KeyResolver switch
        {
            ThrottlingKeyResolver.Host => request.RequestUri?.Host ?? "unknown",
            ThrottlingKeyResolver.Uri => request.RequestUri?.ToString() ?? "unknown",
            _ => request.RequestUri?.Host ?? "unknown"
        };
    }

    private async Task<IRateLimiter> GetOrCreateRateLimiterAsync(string key, CancellationToken cancellationToken)
    {
        await _rateLimitersLock.WaitAsync(cancellationToken);
        try
        {
            if (_rateLimiters.TryGetValue(key, out var existingLimiter))
            {
                return existingLimiter;
            }

            var policyConfig = GetPolicyConfiguration(key);
            var rateLimiter = CreateRateLimiter(policyConfig);
            _rateLimiters[key] = rateLimiter;

            _logger.LogDebug("Created rate limiter for key: {Key} with policy: {Policy}", key, policyConfig.Algorithm);
            return rateLimiter;
        }
        finally
        {
            _rateLimitersLock.Release();
        }
    }

    private ThrottlingPolicyConfiguration GetPolicyConfiguration(string key)
    {
        // Try to find a specific policy for this key
        if (_options.Policies.TryGetValue(key, out var specificPolicy))
        {
            return specificPolicy;
        }

        // Use default policy
        if (_options.Policies.TryGetValue(_options.DefaultPolicy, out var defaultPolicy))
        {
            return defaultPolicy;
        }

        // Fallback to a default configuration
        return new ThrottlingPolicyConfiguration
        {
            Algorithm = "TokenBucket",
            Capacity = 100,
            RefillRatePerSecond = 10.0
        };
    }

    private IRateLimiter CreateRateLimiter(ThrottlingPolicyConfiguration config)
    {
        return config.Algorithm.ToLowerInvariant() switch
        {
            "tokenbucket" => new TokenBucketPolicy(
                $"httpclient-{config.Algorithm}",
                config.Capacity,
                config.RefillRatePerSecond,
                _storage,
                _logger as ILogger<TokenBucketPolicy>),

            "leakybucket" => new LeakyBucketPolicy(
                $"httpclient-{config.Algorithm}",
                config.Capacity,
                config.RefillRatePerSecond,
                _storage,
                _logger as ILogger<LeakyBucketPolicy>),

            "fixedwindow" => new FixedWindowPolicy(
                $"httpclient-{config.Algorithm}",
                config.Capacity,
                config.WindowSize,
                _storage,
                _logger as ILogger<FixedWindowPolicy>),

            "slidingwindow" => new SlidingWindowPolicy(
                $"httpclient-{config.Algorithm}",
                config.Capacity,
                config.WindowSize,
                _storage,
                _logger as ILogger<SlidingWindowPolicy>),

            _ => throw new NotSupportedException($"Unsupported throttling algorithm: {config.Algorithm}")
        };
    }

    private TimeSpan CalculateRetryDelay(int attempt)
    {
        if (!_options.UseExponentialBackoff)
        {
            return _options.RetryDelay;
        }

        var delay = TimeSpan.FromMilliseconds(
            _options.RetryDelay.TotalMilliseconds * Math.Pow(2, attempt - 1));

        return delay > _options.MaxRetryDelay ? _options.MaxRetryDelay : delay;
    }

    /// <summary>
    /// Disposes the handler and cleans up resources.
    /// </summary>
    /// <param name="disposing">True if disposing managed resources.</param>
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _rateLimitersLock?.Dispose();
            foreach (var rateLimiter in _rateLimiters.Values)
            {
                if (rateLimiter is IDisposable disposable)
                {
                    disposable.Dispose();
                }
            }
            _rateLimiters.Clear();
        }
        base.Dispose(disposing);
    }
}
