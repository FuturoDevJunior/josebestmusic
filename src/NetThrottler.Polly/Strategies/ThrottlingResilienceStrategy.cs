using Microsoft.Extensions.Logging;
using NetThrottler.Core.Interfaces;
using Polly;
using Polly.Extensions.Http;

namespace NetThrottler.Polly.Strategies;

/// <summary>
/// A resilience strategy that combines rate limiting with Polly resilience patterns.
/// </summary>
public class ThrottlingResilienceStrategy
{
    private readonly IThrottleStorage _storage;
    private readonly ILogger<ThrottlingResilienceStrategy> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="ThrottlingResilienceStrategy"/> class.
    /// </summary>
    /// <param name="storage">The throttle storage instance.</param>
    /// <param name="logger">The logger instance.</param>
    public ThrottlingResilienceStrategy(IThrottleStorage storage, ILogger<ThrottlingResilienceStrategy> logger)
    {
        _storage = storage ?? throw new ArgumentNullException(nameof(storage));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Creates a comprehensive resilience strategy that includes rate limiting, retry, and circuit breaker patterns.
    /// </summary>
    /// <param name="rateLimiter">The rate limiter to use.</param>
    /// <param name="retryCount">The number of retry attempts.</param>
    /// <param name="circuitBreakerThreshold">The circuit breaker failure threshold.</param>
    /// <param name="circuitBreakerDuration">The circuit breaker duration.</param>
    /// <returns>A resilience strategy builder.</returns>
    public ResilienceStrategyBuilder CreateComprehensiveStrategy(
        IRateLimiter rateLimiter,
        int retryCount = 3,
        int circuitBreakerThreshold = 5,
        TimeSpan? circuitBreakerDuration = null)
    {
        var duration = circuitBreakerDuration ?? TimeSpan.FromSeconds(30);

        return new ResilienceStrategyBuilder()
            .AddRetry(new Polly.Retry.RetryStrategyOptions
            {
                ShouldHandle = new PredicateBuilder().Handle<HttpRequestException>(),
                MaxRetryAttempts = retryCount,
                Delay = TimeSpan.FromSeconds(1),
                BackoffType = DelayBackoffType.Exponential,
                MaxDelay = TimeSpan.FromSeconds(10)
            })
            .AddCircuitBreaker(new Polly.CircuitBreaker.CircuitBreakerStrategyOptions
            {
                ShouldHandle = new PredicateBuilder().Handle<HttpRequestException>(),
                FailureThreshold = circuitBreakerThreshold,
                SamplingDuration = duration,
                MinimumThroughput = 2,
                BreakDuration = duration
            })
            .AddRateLimiter(new Polly.RateLimiting.RateLimiterStrategyOptions
            {
                RateLimiter = new ThrottlingRateLimiter(rateLimiter, _logger)
            });
    }

    /// <summary>
    /// Creates a rate limiting strategy.
    /// </summary>
    /// <param name="rateLimiter">The rate limiter to use.</param>
    /// <returns>A resilience strategy builder with rate limiting.</returns>
    public ResilienceStrategyBuilder CreateRateLimitingStrategy(IRateLimiter rateLimiter)
    {
        return new ResilienceStrategyBuilder()
            .AddRateLimiter(new Polly.RateLimiting.RateLimiterStrategyOptions
            {
                RateLimiter = new ThrottlingRateLimiter(rateLimiter, _logger)
            });
    }

    /// <summary>
    /// Creates a retry strategy with rate limiting.
    /// </summary>
    /// <param name="rateLimiter">The rate limiter to use.</param>
    /// <param name="retryCount">The number of retry attempts.</param>
    /// <returns>A resilience strategy builder with retry and rate limiting.</returns>
    public ResilienceStrategyBuilder CreateRetryWithRateLimitingStrategy(IRateLimiter rateLimiter, int retryCount = 3)
    {
        return new ResilienceStrategyBuilder()
            .AddRetry(new Polly.Retry.RetryStrategyOptions
            {
                ShouldHandle = new PredicateBuilder().Handle<HttpRequestException>(),
                MaxRetryAttempts = retryCount,
                Delay = TimeSpan.FromSeconds(1),
                BackoffType = DelayBackoffType.Exponential,
                MaxDelay = TimeSpan.FromSeconds(10)
            })
            .AddRateLimiter(new Polly.RateLimiting.RateLimiterStrategyOptions
            {
                RateLimiter = new ThrottlingRateLimiter(rateLimiter, _logger)
            });
    }
}

/// <summary>
/// A rate limiter implementation that integrates with Polly's rate limiting.
/// </summary>
public class ThrottlingRateLimiter : Polly.RateLimiting.RateLimiter
{
    private readonly IRateLimiter _rateLimiter;
    private readonly ILogger _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="ThrottlingRateLimiter"/> class.
    /// </summary>
    /// <param name="rateLimiter">The rate limiter to use.</param>
    /// <param name="logger">The logger instance.</param>
    public ThrottlingRateLimiter(IRateLimiter rateLimiter, ILogger logger)
    {
        _rateLimiter = rateLimiter ?? throw new ArgumentNullException(nameof(rateLimiter));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Acquires a permit from the rate limiter.
    /// </summary>
    /// <param name="permitCount">The number of permits to acquire.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A rate limiter lease.</returns>
    protected override async ValueTask<Polly.RateLimiting.RateLimiterLease> AcquireAsyncCore(int permitCount, CancellationToken cancellationToken)
    {
        var key = "polly-throttling"; // You might want to make this configurable
        var isAllowed = await _rateLimiter.TryAcquireAsync(key, permitCount, cancellationToken);

        if (isAllowed)
        {
            _logger.LogDebug("Rate limiter permit acquired: {PermitCount}", permitCount);
            return new ThrottlingRateLimiterLease(true, permitCount);
        }

        _logger.LogWarning("Rate limiter permit denied: {PermitCount}", permitCount);
        return new ThrottlingRateLimiterLease(false, permitCount);
    }

    /// <summary>
    /// Attempts to acquire a permit from the rate limiter.
    /// </summary>
    /// <param name="permitCount">The number of permits to acquire.</param>
    /// <returns>A rate limiter lease.</returns>
    protected override Polly.RateLimiting.RateLimiterLease AttemptAcquireCore(int permitCount)
    {
        // For synchronous operations, we'll use a simple approach
        // In a real implementation, you might want to cache the result
        return new ThrottlingRateLimiterLease(false, permitCount);
    }
}

/// <summary>
/// A rate limiter lease implementation for throttling.
/// </summary>
public class ThrottlingRateLimiterLease : Polly.RateLimiting.RateLimiterLease
{
    private readonly bool _isAcquired;
    private readonly int _permitCount;

    /// <summary>
    /// Initializes a new instance of the <see cref="ThrottlingRateLimiterLease"/> class.
    /// </summary>
    /// <param name="isAcquired">Whether the permit was acquired.</param>
    /// <param name="permitCount">The number of permits.</param>
    public ThrottlingRateLimiterLease(bool isAcquired, int permitCount)
    {
        _isAcquired = isAcquired;
        _permitCount = permitCount;
    }

    /// <summary>
    /// Gets whether the permit was acquired.
    /// </summary>
    public override bool IsAcquired => _isAcquired;

    /// <summary>
    /// Gets the number of permits.
    /// </summary>
    public override int PermitCount => _permitCount;

    /// <summary>
    /// Disposes the lease.
    /// </summary>
    protected override void Dispose(bool disposing)
    {
        // Nothing to dispose in this implementation
    }
}
