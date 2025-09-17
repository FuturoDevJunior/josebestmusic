using Microsoft.Extensions.Logging;
using NetThrottler.Core.Interfaces;
using System.Collections.Concurrent;

namespace NetThrottler.Core.Policies;

/// <summary>
/// Implements the Sliding Window rate limiting algorithm.
/// Requests are counted within a sliding time window, providing more accurate rate limiting.
/// </summary>
public sealed class SlidingWindowPolicy : IPolicy, IRateLimiter, IDisposable
{
    private readonly string _name;
    private readonly int _limit;
    private readonly TimeSpan _windowSize;
    private readonly IThrottleStorage _storage;
    private readonly ILogger<SlidingWindowPolicy> _logger;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new();
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="SlidingWindowPolicy"/> class.
    /// </summary>
    /// <param name="name">The name of the policy.</param>
    /// <param name="limit">The maximum number of requests allowed per window.</param>
    /// <param name="windowSize">The size of the sliding time window.</param>
    /// <param name="storage">The storage implementation for persisting window state.</param>
    /// <param name="logger">The logger instance.</param>
    public SlidingWindowPolicy(
        string name,
        int limit,
        TimeSpan windowSize,
        IThrottleStorage storage,
        ILogger<SlidingWindowPolicy>? logger = null)
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
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<SlidingWindowPolicy>.Instance;

        _logger.LogInformation("SlidingWindowPolicy '{Name}' initialized with limit: {Limit}, window size: {WindowSize}",
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
    /// Gets the algorithm type.
    /// </summary>
    public string Algorithm => "SlidingWindow";

    /// <summary>
    /// Gets the maximum number of requests allowed per time window.
    /// </summary>
    public int MaxRequests => _limit;

    /// <summary>
    /// Gets the time window duration.
    /// </summary>
    public TimeSpan Window => _windowSize;

    /// <summary>
    /// Gets additional configuration parameters.
    /// </summary>
    public IReadOnlyDictionary<string, object> Parameters { get; } = new Dictionary<string, object>();

    /// <summary>
    /// Attempts to acquire the specified number of permits within the sliding window.
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
            var windowStart = currentTime.Subtract(_windowSize);

            // Get current count within the sliding window
            var currentCount = await GetCurrentCountAsync(key, windowStart, currentTime, cancellationToken);

            // Check if we can accommodate the requested permits
            if (currentCount + permits <= _limit)
            {
                // Record the new request
                await RecordRequestAsync(key, currentTime, permits, cancellationToken);

                _logger.LogDebug("SlidingWindow '{Name}' for key '{Key}': acquired {Permits} permits, count: {Count}/{Limit}, window: {WindowStart} - {WindowEnd}",
                    _name, key, permits, currentCount + permits, _limit, windowStart, currentTime);

                return true;
            }

            _logger.LogDebug("SlidingWindow '{Name}' for key '{Key}': limit exceeded, count: {Count}/{Limit}, requested: {Permits}, window: {WindowStart} - {WindowEnd}",
                _name, key, currentCount, _limit, permits, windowStart, currentTime);

            return false;
        }
        finally
        {
            semaphore.Release();
        }
    }

    /// <summary>
    /// Gets the current state of the sliding window for the specified key.
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
        var windowStart = currentTime.Subtract(_windowSize);
        var currentCount = await GetCurrentCountAsync(key, windowStart, currentTime, cancellationToken);

        return new RateLimitState(
            key,
            (int)(_limit - currentCount),
            currentTime.Add(_windowSize),
            _limit);
    }

    private async Task<long> GetCurrentCountAsync(string key, DateTimeOffset windowStart, DateTimeOffset currentTime, CancellationToken cancellationToken)
    {
        var requests = await GetRequestsInWindowAsync(key, windowStart, currentTime, cancellationToken);
        return requests.Sum(r => r.Count);
    }

    private async Task<List<RequestRecord>> GetRequestsInWindowAsync(string key, DateTimeOffset windowStart, DateTimeOffset currentTime, CancellationToken cancellationToken)
    {
        var requests = new List<RequestRecord>();
        var storageKey = GetStorageKey(key);
        var stateJson = await _storage.GetAsync(storageKey, cancellationToken);

        if (!string.IsNullOrEmpty(stateJson))
        {
            var state = System.Text.Json.JsonSerializer.Deserialize<SlidingWindowState>(stateJson);
            if (state?.Requests != null)
            {
                // Filter requests within the current window
                requests = state.Requests
                    .Where(r => r.Timestamp >= windowStart && r.Timestamp <= currentTime)
                    .ToList();
            }
        }

        return requests;
    }

    private async Task RecordRequestAsync(string key, DateTimeOffset timestamp, int count, CancellationToken cancellationToken)
    {
        var storageKey = GetStorageKey(key);
        var stateJson = await _storage.GetAsync(storageKey, cancellationToken);

        var state = new SlidingWindowState();
        if (!string.IsNullOrEmpty(stateJson))
        {
            state = System.Text.Json.JsonSerializer.Deserialize<SlidingWindowState>(stateJson) ?? new SlidingWindowState();
        }

        // Add the new request
        state.Requests ??= new List<RequestRecord>();
        state.Requests.Add(new RequestRecord { Timestamp = timestamp, Count = count });

        // Clean up old requests (older than 2 * window size to be safe)
        var cutoffTime = timestamp.Subtract(TimeSpan.FromTicks(_windowSize.Ticks * 2));
        state.Requests = state.Requests.Where(r => r.Timestamp > cutoffTime).ToList();

        // Update the state
        state.WindowStart = timestamp.Subtract(_windowSize);
        state.WindowEnd = timestamp;
        state.CurrentCount = state.Requests.Sum(r => r.Count);

        var updatedStateJson = System.Text.Json.JsonSerializer.Serialize(state);
        await _storage.SetAsync(storageKey, updatedStateJson, TimeSpan.FromTicks(_windowSize.Ticks * 2), cancellationToken);
    }

    private string GetStorageKey(string key) => $"slidingwindow:{_name}:{key}";
    private string GetLockKey(string key) => $"slidingwindow_lock:{_name}:{key}";

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
/// Represents the state of a sliding window.
/// </summary>
public class SlidingWindowState
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

    /// <summary>
    /// Gets or sets the list of request records.
    /// </summary>
    public List<RequestRecord> Requests { get; set; } = new();
}

/// <summary>
/// Represents a request record in the sliding window.
/// </summary>
public class RequestRecord
{
    /// <summary>
    /// Gets or sets the timestamp of the request.
    /// </summary>
    public DateTimeOffset Timestamp { get; set; }

    /// <summary>
    /// Gets or sets the count of requests at this timestamp.
    /// </summary>
    public int Count { get; set; }
}
