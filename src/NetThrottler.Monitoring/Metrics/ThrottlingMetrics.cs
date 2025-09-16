using Microsoft.Extensions.Logging;
using NetThrottler.Core.Interfaces;
using Prometheus;

namespace NetThrottler.Monitoring.Metrics;

/// <summary>
/// Provides metrics collection for throttling operations.
/// </summary>
public class ThrottlingMetrics
{
    private static readonly Counter RequestCounter = Metrics
        .CreateCounter("netthrottler_requests_total", "Total number of throttling requests", new[] { "policy", "key", "result" });

    private static readonly Histogram RequestDuration = Metrics
        .CreateHistogram("netthrottler_request_duration_seconds", "Duration of throttling requests", new[] { "policy", "key" });

    private static readonly Gauge CurrentRequests = Metrics
        .CreateGauge("netthrottler_current_requests", "Current number of active requests", new[] { "policy", "key" });

    private static readonly Counter RateLimitedRequests = Metrics
        .CreateCounter("netthrottler_rate_limited_requests_total", "Total number of rate limited requests", new[] { "policy", "key" });

    private static readonly Gauge StorageOperations = Metrics
        .CreateGauge("netthrottler_storage_operations_total", "Total number of storage operations", new[] { "operation", "storage_type" });

    private static readonly Histogram StorageOperationDuration = Metrics
        .CreateHistogram("netthrottler_storage_operation_duration_seconds", "Duration of storage operations", new[] { "operation", "storage_type" });

    private readonly ILogger<ThrottlingMetrics> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="ThrottlingMetrics"/> class.
    /// </summary>
    /// <param name="logger">The logger instance.</param>
    public ThrottlingMetrics(ILogger<ThrottlingMetrics> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Records a throttling request.
    /// </summary>
    /// <param name="policyName">The name of the policy.</param>
    /// <param name="key">The throttling key.</param>
    /// <param name="allowed">Whether the request was allowed.</param>
    /// <param name="duration">The duration of the request.</param>
    public void RecordRequest(string policyName, string key, bool allowed, TimeSpan duration)
    {
        try
        {
            var result = allowed ? "allowed" : "rate_limited";
            RequestCounter.WithLabels(policyName, key, result).Inc();
            RequestDuration.WithLabels(policyName, key).Observe(duration.TotalSeconds);

            if (!allowed)
            {
                RateLimitedRequests.WithLabels(policyName, key).Inc();
            }

            _logger.LogDebug("Recorded throttling request: {Policy} - {Key} - {Result} - {Duration}ms",
                policyName, key, result, duration.TotalMilliseconds);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error recording throttling request metrics");
        }
    }

    /// <summary>
    /// Records a storage operation.
    /// </summary>
    /// <param name="operation">The storage operation.</param>
    /// <param name="storageType">The type of storage.</param>
    /// <param name="duration">The duration of the operation.</param>
    public void RecordStorageOperation(string operation, string storageType, TimeSpan duration)
    {
        try
        {
            StorageOperations.WithLabels(operation, storageType).Inc();
            StorageOperationDuration.WithLabels(operation, storageType).Observe(duration.TotalSeconds);

            _logger.LogDebug("Recorded storage operation: {Operation} - {StorageType} - {Duration}ms",
                operation, storageType, duration.TotalMilliseconds);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error recording storage operation metrics");
        }
    }

    /// <summary>
    /// Increments the current requests counter.
    /// </summary>
    /// <param name="policyName">The name of the policy.</param>
    /// <param name="key">The throttling key.</param>
    public void IncrementCurrentRequests(string policyName, string key)
    {
        try
        {
            CurrentRequests.WithLabels(policyName, key).Inc();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error incrementing current requests counter");
        }
    }

    /// <summary>
    /// Decrements the current requests counter.
    /// </summary>
    /// <param name="policyName">The name of the policy.</param>
    /// <param name="key">The throttling key.</param>
    public void DecrementCurrentRequests(string policyName, string key)
    {
        try
        {
            CurrentRequests.WithLabels(policyName, key).Dec();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error decrementing current requests counter");
        }
    }
}

/// <summary>
/// A decorator for IThrottleStorage that adds metrics collection.
/// </summary>
public class MetricsThrottleStorage : IThrottleStorage
{
    private readonly IThrottleStorage _innerStorage;
    private readonly ThrottlingMetrics _metrics;
    private readonly ILogger<MetricsThrottleStorage> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="MetricsThrottleStorage"/> class.
    /// </summary>
    /// <param name="innerStorage">The inner storage implementation.</param>
    /// <param name="metrics">The metrics collector.</param>
    /// <param name="logger">The logger instance.</param>
    public MetricsThrottleStorage(IThrottleStorage innerStorage, ThrottlingMetrics metrics, ILogger<MetricsThrottleStorage> logger)
    {
        _innerStorage = innerStorage ?? throw new ArgumentNullException(nameof(innerStorage));
        _metrics = metrics ?? throw new ArgumentNullException(nameof(metrics));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Gets a stored value with metrics collection.
    /// </summary>
    /// <param name="key">The key to retrieve.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The stored value, or null if not found.</returns>
    public async Task<string?> GetAsync(string key, CancellationToken cancellationToken = default)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            var result = await _innerStorage.GetAsync(key, cancellationToken);
            return result;
        }
        finally
        {
            stopwatch.Stop();
            _metrics.RecordStorageOperation("get", _innerStorage.GetType().Name, stopwatch.Elapsed);
        }
    }

    /// <summary>
    /// Sets a value with metrics collection.
    /// </summary>
    /// <param name="key">The key to set.</param>
    /// <param name="value">The value to store.</param>
    /// <param name="ttl">The time-to-live for the key.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task SetAsync(string key, string value, TimeSpan? ttl = null, CancellationToken cancellationToken = default)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            await _innerStorage.SetAsync(key, value, ttl, cancellationToken);
        }
        finally
        {
            stopwatch.Stop();
            _metrics.RecordStorageOperation("set", _innerStorage.GetType().Name, stopwatch.Elapsed);
        }
    }

    /// <summary>
    /// Removes a key with metrics collection.
    /// </summary>
    /// <param name="key">The key to remove.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task RemoveAsync(string key, CancellationToken cancellationToken = default)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            await _innerStorage.RemoveAsync(key, cancellationToken);
        }
        finally
        {
            stopwatch.Stop();
            _metrics.RecordStorageOperation("remove", _innerStorage.GetType().Name, stopwatch.Elapsed);
        }
    }

    /// <summary>
    /// Checks if a key exists with metrics collection.
    /// </summary>
    /// <param name="key">The key to check.</param>
    /// <returns>True if the key exists, false otherwise.</returns>
    public async Task<bool> ExistsAsync(string key)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            return await _innerStorage.ExistsAsync(key);
        }
        finally
        {
            stopwatch.Stop();
            _metrics.RecordStorageOperation("exists", _innerStorage.GetType().Name, stopwatch.Elapsed);
        }
    }

    /// <summary>
    /// Increments a value with metrics collection.
    /// </summary>
    /// <param name="key">The key to increment.</param>
    /// <param name="incrementBy">The amount to increment by.</param>
    /// <param name="ttl">The time-to-live for the key.</param>
    /// <returns>The new value after incrementing.</returns>
    public async Task<long> IncrementAsync(string key, long incrementBy = 1, TimeSpan? ttl = null)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            return await _innerStorage.IncrementAsync(key, incrementBy, ttl);
        }
        finally
        {
            stopwatch.Stop();
            _metrics.RecordStorageOperation("increment", _innerStorage.GetType().Name, stopwatch.Elapsed);
        }
    }

    /// <summary>
    /// Decrements a value with metrics collection.
    /// </summary>
    /// <param name="key">The key to decrement.</param>
    /// <param name="decrementBy">The amount to decrement by.</param>
    /// <param name="ttl">The time-to-live for the key.</param>
    /// <returns>The new value after decrementing.</returns>
    public async Task<long> DecrementAsync(string key, long decrementBy = 1, TimeSpan? ttl = null)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            return await _innerStorage.DecrementAsync(key, decrementBy, ttl);
        }
        finally
        {
            stopwatch.Stop();
            _metrics.RecordStorageOperation("decrement", _innerStorage.GetType().Name, stopwatch.Elapsed);
        }
    }
}
