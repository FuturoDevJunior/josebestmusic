using Microsoft.Extensions.Logging;
using NetThrottler.Core.Interfaces;
using System.Collections.Concurrent;

namespace NetThrottler.Core.Policies;

/// <summary>
/// Implements the Fixed Window rate limiting algorithm.
/// Requests are counted within fixed time windows, with counters resetting at window boundaries.
/// </summary>
public sealed class FixedWindowPolicy : IRateLimiter, IDisposable
{
    private readonly string _name;
    private readonly int _limit;
    private readonly TimeSpan _windowSize;
    private readonly IThrottleStorage _storage;
    private readonly ILogger<FixedWindowPolicy> _logger;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new();
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="FixedWindowPolicy"/> class.
    /// </summary>
    /// <param name="name">The name of the policy.</param>
    /// <param name="limit">The maximum number of requests allowed per window.</param>
    /// <param name="windowSize">The size of the time window.</param>
    /// <param name="storage">The storage implementation for persisting window state.</param>
    /// <param name="logger">The logger instance.</param>
    public FixedWindowPolicy(
        string name,
        int limit,
        TimeSpan windowSize,
        IThrottleStorage storage,
        ILogger<FixedWindowPolicy>? logger = null)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Policy name cannot be null or empty.", nameof(name));
        if (limit <= 0)
            throw new ArgumentOutOfRangeException(nameof(limit), "Limit must be greater than zero.");
        if (windowSize <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(windowSize), "Window size must be greater than zero.");
        if (storage == null)
            throw new ArgumentNullException(nameof(storage));

        _name = name;
        _limit = limit;
        _windowSize = windowSize;
        _storage = storage;
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<FixedWindowPolicy>.Instance;

        _logger.LogInformation("FixedWindowPolicy '{Name}' initialized with limit: {Limit}, window size: {WindowSize}",
            _name, _limit, _windowSize);
    }

    /// <summary>
    /// Gets the name of the policy.
    /// </summary>
    public string Name => _name;

    /// <summary>
    /// Gets the limit of requests per window.
    /// </summary>
    public int Limit => _limit;

    /// <summary>
    /// Gets the window size.
    /// </summary>
    public TimeSpan WindowSize => _windowSize;

    /// <summary>
    /// Attempts to acquire the specified number of permits within the current window.
    /// </summary>
    /// <param name="key">The key to identify the window.</param>
    /// <param name="permits">The number of permits to acquire.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>True if the permits were acquired, false if the window limit would be exceeded.</returns>
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
            var currentTime = DateTimeOffset.UtcNow;
            var windowStart = GetWindowStart(currentTime);
            var windowKey = GetWindowKey(key, windowStart);

            // Get current count for this window
            var currentCount = await GetCurrentCountAsync(windowKey, cancellationToken);

            // Check if we can accommodate the requested permits
            if (currentCount + permits <= _limit)
            {
                // Increment the count
                var newCount = await IncrementCountAsync(windowKey, permits, cancellationToken);

                _logger.LogDebug("FixedWindow '{Name}' for key '{Key}': acquired {Permits} permits, count: {Count}/{Limit}, window: {WindowStart}",
                    _name, key, permits, newCount, _limit, windowStart);

                return true;
            }

            _logger.LogDebug("FixedWindow '{Name}' for key '{Key}': limit exceeded, count: {Count}/{Limit}, requested: {Permits}, window: {WindowStart}",
                _name, key, currentCount, _limit, permits, windowStart);

            return false;
        }
        finally
        {
            semaphore.Release();
        }
    }

    /// <summary>
    /// Gets the current state of the fixed window for the specified key.
    /// </summary>
    /// <param name="key">The key to identify the window.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The current window state, or null if no state exists.</returns>
    public async Task<RateLimitState?> GetStateAsync(string key, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(key))
            throw new ArgumentException("Key cannot be null or empty.", nameof(key));

        cancellationToken.ThrowIfCancellationRequested();

        var currentTime = DateTimeOffset.UtcNow;
        var windowStart = GetWindowStart(currentTime);
        var windowKey = GetWindowKey(key, windowStart);

        var currentCount = await GetCurrentCountAsync(windowKey, cancellationToken);
        var windowEnd = windowStart.Add(_windowSize);

        return new RateLimitState(
            key,
            (int)(_limit - currentCount),
            windowEnd,
            _limit);
    }

    private DateTimeOffset GetWindowStart(DateTimeOffset currentTime)
    {
        var windowStartTicks = (currentTime.Ticks / _windowSize.Ticks) * _windowSize.Ticks;
        return new DateTimeOffset(windowStartTicks, currentTime.Offset);
    }

    private async Task<long> GetCurrentCountAsync(string windowKey, CancellationToken cancellationToken)
    {
        var countStr = await _storage.GetAsync(windowKey, cancellationToken);
        return long.TryParse(countStr, out var count) ? count : 0;
    }

    private async Task<long> IncrementCountAsync(string windowKey, int increment, CancellationToken cancellationToken)
    {
        var newCount = await _storage.IncrementAsync(windowKey, increment, _windowSize);
        return newCount;
    }

    private string GetWindowKey(string key, DateTimeOffset windowStart)
    {
        var windowId = windowStart.ToUnixTimeSeconds();
        return $"fixedwindow:{_name}:{key}:{windowId}";
    }

    private string GetLockKey(string key) => $"fixedwindow_lock:{_name}:{key}";

    /// <summary>
    /// Disposes the policy and cleans up resources.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
            return;

        foreach (var semaphore in _locks.Values)
        {
            semaphore?.Dispose();
        }
        _locks.Clear();

        _disposed = true;
    }
}

/// <summary>
/// Represents the state of a fixed window.
/// </summary>
public class WindowState
{
    /// <summary>
    /// Gets or sets the current count of requests in the window.
    /// </summary>
    public long CurrentCount { get; set; }

    /// <summary>
    /// Gets or sets the limit of requests per window.
    /// </summary>
    public int Limit { get; set; }

    /// <summary>
    /// Gets or sets the start time of the current window.
    /// </summary>
    public DateTimeOffset WindowStart { get; set; }

    /// <summary>
    /// Gets or sets the end time of the current window.
    /// </summary>
    public DateTimeOffset WindowEnd { get; set; }

    /// <summary>
    /// Gets or sets the time when the window will reset.
    /// </summary>
    public DateTimeOffset ResetTime { get; set; }
}
