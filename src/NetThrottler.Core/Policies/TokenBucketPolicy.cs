using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using NetThrottler.Core.Interfaces;

namespace NetThrottler.Core.Policies;

/// <summary>
/// Token Bucket rate limiting policy implementation.
/// Allows bursts up to the bucket capacity and refills at a steady rate.
/// Thread-safe with per-key locking for optimal performance.
/// </summary>
public sealed class TokenBucketPolicy : IPolicy, IRateLimiter, IDisposable
{
    private readonly IThrottleStorage _storage;
    private readonly ILogger<TokenBucketPolicy>? _logger;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new();

    /// <summary>
    /// Gets the unique name of this policy.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Gets the algorithm type.
    /// </summary>
    public string Algorithm => "TokenBucket";

    /// <summary>
    /// Gets the maximum number of requests allowed per time window.
    /// </summary>
    public int MaxRequests { get; }

    /// <summary>
    /// Gets the time window duration.
    /// </summary>
    public TimeSpan Window { get; }

    /// <summary>
    /// Gets additional configuration parameters.
    /// </summary>
    public IReadOnlyDictionary<string, object> Parameters { get; }

    /// <summary>
    /// Gets the bucket capacity (maximum tokens).
    /// </summary>
    public double Capacity { get; }

    /// <summary>
    /// Gets the refill rate in tokens per second.
    /// </summary>
    public double RefillRatePerSecond { get; }

    /// <summary>
    /// Initializes a new instance of TokenBucketPolicy.
    /// </summary>
    /// <param name="name">Policy name</param>
    /// <param name="capacity">Bucket capacity (maximum tokens)</param>
    /// <param name="refillRatePerSecond">Tokens refilled per second</param>
    /// <param name="storage">Storage implementation</param>
    /// <param name="logger">Logger instance (optional)</param>
    public TokenBucketPolicy(
        string name,
        double capacity,
        double refillRatePerSecond,
        IThrottleStorage storage,
        ILogger<TokenBucketPolicy>? logger = null)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Policy name cannot be null or empty", nameof(name));
        if (capacity <= 0)
            throw new ArgumentOutOfRangeException(nameof(capacity), "Capacity must be greater than 0");
        if (refillRatePerSecond < 0)
            throw new ArgumentOutOfRangeException(nameof(refillRatePerSecond), "Refill rate cannot be negative");
        if (storage == null)
            throw new ArgumentNullException(nameof(storage));

        Name = name;
        Capacity = capacity;
        RefillRatePerSecond = refillRatePerSecond;
        _storage = storage;
        _logger = logger;

        // Calculate derived values
        MaxRequests = (int)Math.Ceiling(capacity);
        Window = TimeSpan.FromSeconds(capacity / Math.Max(refillRatePerSecond, 1));

        Parameters = new Dictionary<string, object>
        {
            ["Capacity"] = capacity,
            ["RefillRatePerSecond"] = refillRatePerSecond
        };
    }

    /// <summary>
    /// Initializes a new instance from policy configuration.
    /// </summary>
    /// <param name="config">Policy configuration</param>
    /// <param name="storage">Storage implementation</param>
    /// <param name="logger">Logger instance (optional)</param>
    public TokenBucketPolicy(
        PolicyConfiguration config,
        IThrottleStorage storage,
        ILogger<TokenBucketPolicy>? logger = null)
        : this(
            config.Name,
            GetCapacityFromConfig(config),
            GetRefillRateFromConfig(config),
            storage,
            logger)
    {
    }

    /// <inheritdoc />
    public async Task<bool> TryAcquireAsync(string key, int permits = 1, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(key))
            throw new ArgumentException("Key cannot be null or empty", nameof(key));
        if (permits <= 0)
            throw new ArgumentOutOfRangeException(nameof(permits), "Permits must be greater than 0");

        var semaphore = _locks.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));

        await semaphore.WaitAsync(cancellationToken);
        try
        {
            var state = await GetOrCreateBucketStateAsync(key, cancellationToken);
            var now = DateTimeOffset.UtcNow;

            // Refill tokens based on elapsed time
            RefillTokens(state, now);

            if (state.Tokens >= permits)
            {
                state.Tokens -= permits;
                state.LastRefill = now;

                await PersistBucketStateAsync(key, state, cancellationToken);

                _logger?.LogDebug("Acquired {Permits} tokens for key: {Key}, remaining: {Remaining}",
                    permits, key, state.Tokens);

                return true;
            }

            // Not enough tokens available
            await PersistBucketStateAsync(key, state, cancellationToken);

            _logger?.LogDebug("Rate limited key: {Key}, requested: {Permits}, available: {Available}",
                key, permits, state.Tokens);

            return false;
        }
        finally
        {
            semaphore.Release();
        }
    }

    /// <inheritdoc />
    public async Task<RateLimitState?> GetStateAsync(string key, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(key))
            throw new ArgumentException("Key cannot be null or empty", nameof(key));

        try
        {
            var state = await GetBucketStateAsync(key, cancellationToken);
            if (state == null)
                return null;

            var now = DateTimeOffset.UtcNow;
            RefillTokens(state, now);

            var resetTime = state.LastRefill.AddSeconds((Capacity - state.Tokens) / Math.Max(RefillRatePerSecond, 0.001));

            return new RateLimitState(
                key,
                (int)Math.Floor(state.Tokens),
                resetTime,
                MaxRequests);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error getting state for key: {Key}", key);
            return null;
        }
    }

    private async Task<BucketState> GetOrCreateBucketStateAsync(string key, CancellationToken cancellationToken)
    {
        var state = await GetBucketStateAsync(key, cancellationToken);
        if (state == null)
        {
            state = new BucketState
            {
                Tokens = Capacity,
                LastRefill = DateTimeOffset.UtcNow
            };
        }
        return state;
    }

    private async Task<BucketState?> GetBucketStateAsync(string key, CancellationToken cancellationToken)
    {
        try
        {
            var storageKey = GetStorageKey(key);
            var json = await _storage.GetAsync(storageKey, cancellationToken);

            if (string.IsNullOrEmpty(json))
                return null;

            var dto = JsonSerializer.Deserialize<BucketStateDto>(json);
            if (dto == null)
                return null;

            return new BucketState
            {
                Tokens = double.Parse(dto.Tokens, CultureInfo.InvariantCulture),
                LastRefill = DateTimeOffset.Parse(dto.LastRefill, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal)
            };
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to deserialize bucket state for key: {Key}", key);
            return null;
        }
    }

    private async Task PersistBucketStateAsync(string key, BucketState state, CancellationToken cancellationToken)
    {
        try
        {
            var dto = new BucketStateDto
            {
                Tokens = state.Tokens.ToString("R", CultureInfo.InvariantCulture),
                LastRefill = state.LastRefill.UtcDateTime.ToString("o", CultureInfo.InvariantCulture)
            };

            var json = JsonSerializer.Serialize(dto);
            var storageKey = GetStorageKey(key);
            var ttl = TimeSpan.FromMinutes(5); // Keep state for 5 minutes

            await _storage.SetAsync(storageKey, json, ttl, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to persist bucket state for key: {Key}", key);
        }
    }

    private void RefillTokens(BucketState state, DateTimeOffset now)
    {
        var elapsed = (now - state.LastRefill).TotalSeconds;
        if (elapsed <= 0)
            return;

        var tokensToAdd = elapsed * RefillRatePerSecond;
        if (tokensToAdd <= 0)
        {
            state.LastRefill = now;
            return;
        }

        var newTokenCount = Math.Min(Capacity, state.Tokens + tokensToAdd);
        state.Tokens = newTokenCount;
        state.LastRefill = now;
    }

    private static string GetStorageKey(string key) => $"tokenbucket:{key}";

    private static double GetCapacityFromConfig(PolicyConfiguration config)
    {
        if (config.Parameters?.TryGetValue("Capacity", out var capacityObj) == true)
        {
            return Convert.ToDouble(capacityObj);
        }
        return config.MaxRequests;
    }

    private static double GetRefillRateFromConfig(PolicyConfiguration config)
    {
        if (config.Parameters?.TryGetValue("RefillRatePerSecond", out var rateObj) == true)
        {
            return Convert.ToDouble(rateObj);
        }
        // Calculate refill rate based on window and max requests
        return config.MaxRequests / config.Window.TotalSeconds;
    }

    /// <summary>
    /// Disposes the policy and releases all semaphore resources.
    /// </summary>
    public void Dispose()
    {
        foreach (var kvp in _locks)
        {
            kvp.Value.Dispose();
        }
        _locks.Clear();
    }

    private sealed class BucketState
    {
        public double Tokens { get; set; }
        public DateTimeOffset LastRefill { get; set; }
    }

    private sealed class BucketStateDto
    {
        public string Tokens { get; set; } = string.Empty;
        public string LastRefill { get; set; } = string.Empty;
    }

}
