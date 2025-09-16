using Microsoft.Extensions.Logging;
using NetThrottler.Core.Interfaces;
using System.Collections.Concurrent;

namespace NetThrottler.Core.Policies;

/// <summary>
/// Implements the Leaky Bucket rate limiting algorithm.
/// Requests are processed at a constant rate, with excess requests being dropped or queued.
/// </summary>
public sealed class LeakyBucketPolicy : IRateLimiter, IDisposable
{
    private readonly string _name;
    private readonly int _capacity;
    private readonly double _leakRatePerSecond;
    private readonly IThrottleStorage _storage;
    private readonly ILogger<LeakyBucketPolicy> _logger;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new();
    private readonly Timer _leakTimer;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="LeakyBucketPolicy"/> class.
    /// </summary>
    /// <param name="name">The name of the policy.</param>
    /// <param name="capacity">The maximum capacity of the bucket.</param>
    /// <param name="leakRatePerSecond">The rate at which the bucket leaks (requests per second).</param>
    /// <param name="storage">The storage implementation for persisting bucket state.</param>
    /// <param name="logger">The logger instance.</param>
    public LeakyBucketPolicy(
        string name,
        int capacity,
        double leakRatePerSecond,
        IThrottleStorage storage,
        ILogger<LeakyBucketPolicy>? logger = null)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Policy name cannot be null or empty.", nameof(name));
        if (capacity <= 0)
            throw new ArgumentOutOfRangeException(nameof(capacity), "Capacity must be greater than zero.");
        if (leakRatePerSecond <= 0)
            throw new ArgumentOutOfRangeException(nameof(leakRatePerSecond), "Leak rate must be greater than zero.");
        if (storage == null)
            throw new ArgumentNullException(nameof(storage));

        _name = name;
        _capacity = capacity;
        _leakRatePerSecond = leakRatePerSecond;
        _storage = storage;
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<LeakyBucketPolicy>.Instance;

        // Start the leak timer to periodically leak the bucket
        var leakInterval = TimeSpan.FromMilliseconds(1000.0 / leakRatePerSecond);
        _leakTimer = new Timer(LeakBucket, null, leakInterval, leakInterval);

        _logger.LogInformation("LeakyBucketPolicy '{Name}' initialized with capacity: {Capacity}, leak rate: {LeakRate}/sec",
            _name, _capacity, _leakRatePerSecond);
    }

    /// <summary>
    /// Gets the name of the policy.
    /// </summary>
    public string Name => _name;

    /// <summary>
    /// Gets the capacity of the bucket.
    /// </summary>
    public int Capacity => _capacity;

    /// <summary>
    /// Gets the leak rate per second.
    /// </summary>
    public double LeakRatePerSecond => _leakRatePerSecond;

    /// <summary>
    /// Attempts to acquire the specified number of permits from the leaky bucket.
    /// </summary>
    /// <param name="key">The key to identify the bucket.</param>
    /// <param name="permits">The number of permits to acquire.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>True if the permits were acquired, false if the bucket is full.</returns>
    public async Task<bool> TryAcquireAsync(string key, int permits = 1, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(key))
            throw new ArgumentException("Key cannot be null or empty.", nameof(key));
        if (permits <= 0)
            throw new ArgumentOutOfRangeException(nameof(permits), "Permits must be greater than zero.");

        cancellationToken.ThrowIfCancellationRequested();

        var lockKey = GetLockKey(key);
        var semaphore = _locks.GetOrAdd(lockKey, _ => new SemaphoreSlim(1, 1));

        await semaphore.WaitAsync(cancellationToken);
        try
        {
            var bucketState = await GetBucketStateAsync(key, cancellationToken);
            var currentTime = DateTimeOffset.UtcNow;

            // Update the bucket level based on time elapsed since last update
            var timeElapsed = currentTime - bucketState.LastLeakTime;
            var leakedAmount = timeElapsed.TotalSeconds * _leakRatePerSecond;
            var newLevel = Math.Max(0, bucketState.CurrentLevel - leakedAmount);

            // Check if we can accommodate the requested permits
            if (newLevel + permits <= _capacity)
            {
                // Update bucket state
                bucketState.CurrentLevel = newLevel + permits;
                bucketState.LastLeakTime = currentTime;
                bucketState.LastRequestTime = currentTime;

                await SetBucketStateAsync(key, bucketState, cancellationToken);

                _logger.LogDebug("LeakyBucket '{Name}' for key '{Key}': acquired {Permits} permits, new level: {Level}/{Capacity}",
                    _name, key, permits, bucketState.CurrentLevel, _capacity);

                return true;
            }

            _logger.LogDebug("LeakyBucket '{Name}' for key '{Key}': bucket full, level: {Level}/{Capacity}, requested: {Permits}",
                _name, key, newLevel, _capacity, permits);

            return false;
        }
        finally
        {
            semaphore.Release();
        }
    }

    /// <summary>
    /// Gets the current state of the leaky bucket for the specified key.
    /// </summary>
    /// <param name="key">The key to identify the bucket.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The current bucket state, or null if no state exists.</returns>
    public async Task<RateLimitState?> GetStateAsync(string key, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(key))
            throw new ArgumentException("Key cannot be null or empty.", nameof(key));

        cancellationToken.ThrowIfCancellationRequested();

        var bucketState = await GetBucketStateAsync(key, cancellationToken);
        if (bucketState == null)
            return null;

        var currentTime = DateTimeOffset.UtcNow;
        var timeElapsed = currentTime - bucketState.LastLeakTime;
        var leakedAmount = timeElapsed.TotalSeconds * _leakRatePerSecond;
        var currentLevel = Math.Max(0, bucketState.CurrentLevel - leakedAmount);

        return new RateLimitState(
            key,
            (int)(_capacity - currentLevel),
            CalculateResetTime(currentLevel),
            _capacity);
    }

    private async Task<BucketState> GetBucketStateAsync(string key, CancellationToken cancellationToken)
    {
        var storageKey = GetStorageKey(key);
        var stateJson = await _storage.GetAsync(storageKey, cancellationToken);

        if (string.IsNullOrEmpty(stateJson))
        {
            return new BucketState
            {
                CurrentLevel = 0,
                LastLeakTime = DateTimeOffset.UtcNow,
                LastRequestTime = DateTimeOffset.UtcNow
            };
        }

        return System.Text.Json.JsonSerializer.Deserialize<BucketState>(stateJson) ?? new BucketState
        {
            CurrentLevel = 0,
            LastLeakTime = DateTimeOffset.UtcNow,
            LastRequestTime = DateTimeOffset.UtcNow
        };
    }

    private async Task SetBucketStateAsync(string key, BucketState state, CancellationToken cancellationToken)
    {
        var storageKey = GetStorageKey(key);
        var stateJson = System.Text.Json.JsonSerializer.Serialize(state);
        await _storage.SetAsync(storageKey, stateJson, TimeSpan.FromHours(1), cancellationToken);
    }

    private void LeakBucket(object? state)
    {
        // This method is called by the timer to periodically leak the bucket
        // The actual leaking is done in TryAcquireAsync based on time elapsed
        // This timer ensures we don't lose track of time for inactive buckets
    }

    private DateTimeOffset CalculateResetTime(double currentLevel)
    {
        if (currentLevel <= 0)
            return DateTimeOffset.UtcNow;

        var timeToEmpty = currentLevel / _leakRatePerSecond;
        return DateTimeOffset.UtcNow.AddSeconds(timeToEmpty);
    }

    private string GetStorageKey(string key) => $"leakybucket:{_name}:{key}";
    private string GetLockKey(string key) => $"leakybucket_lock:{_name}:{key}";

    /// <summary>
    /// Disposes the policy and cleans up resources.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
            return;

        _leakTimer?.Dispose();
        foreach (var semaphore in _locks.Values)
        {
            semaphore?.Dispose();
        }
        _locks.Clear();

        _disposed = true;
    }
}

/// <summary>
/// Represents the state of a leaky bucket.
/// </summary>
public class BucketState
{
    /// <summary>
    /// Gets or sets the current level of the bucket.
    /// </summary>
    public double CurrentLevel { get; set; }

    /// <summary>
    /// Gets or sets the capacity of the bucket.
    /// </summary>
    public int Capacity { get; set; }

    /// <summary>
    /// Gets or sets the last time the bucket was leaked.
    /// </summary>
    public DateTimeOffset LastLeakTime { get; set; }

    /// <summary>
    /// Gets or sets the last time a request was made.
    /// </summary>
    public DateTimeOffset LastRequestTime { get; set; }

    /// <summary>
    /// Gets or sets the estimated time when the bucket will be empty.
    /// </summary>
    public DateTimeOffset ResetTime { get; set; }
}
