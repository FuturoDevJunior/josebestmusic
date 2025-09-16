using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using NetThrottler.Core.Interfaces;

namespace NetThrottler.Core.Storage;

/// <summary>
/// In-memory implementation of IThrottleStorage using IMemoryCache.
/// Suitable for single-instance applications or testing scenarios.
/// </summary>
public sealed class MemoryThrottleStorage : IThrottleStorage, IDisposable
{
    private readonly IMemoryCache _cache;
    private readonly ILogger<MemoryThrottleStorage>? _logger;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new();
    private readonly MemoryCacheEntryOptions _defaultOptions = new()
    {
        Priority = CacheItemPriority.Normal
    };

    /// <summary>
    /// Initializes a new instance of MemoryThrottleStorage.
    /// </summary>
    /// <param name="cache">Memory cache instance (optional, creates new if null)</param>
    /// <param name="logger">Logger instance (optional)</param>
    public MemoryThrottleStorage(IMemoryCache? cache = null, ILogger<MemoryThrottleStorage>? logger = null)
    {
        _cache = cache ?? new MemoryCache(new MemoryCacheOptions());
        _logger = logger;
    }

    /// <inheritdoc />
    public Task<string?> GetAsync(string key, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(key))
            throw new ArgumentException("Key cannot be null or empty", nameof(key));

        try
        {
            if (_cache.TryGetValue(key, out string? value))
            {
                _logger?.LogDebug("Retrieved value for key: {Key}", key);
                return Task.FromResult(value);
            }

            _logger?.LogDebug("Key not found: {Key}", key);
            return Task.FromResult<string?>(null);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error retrieving key: {Key}", key);
            throw;
        }
    }

    /// <inheritdoc />
    public Task SetAsync(string key, string value, TimeSpan? ttl = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(key))
            throw new ArgumentException("Key cannot be null or empty", nameof(key));

        try
        {
            var options = ttl.HasValue
                ? new MemoryCacheEntryOptions { AbsoluteExpirationRelativeToNow = ttl }
                : _defaultOptions;

            _cache.Set(key, value, options);
            _logger?.LogDebug("Set value for key: {Key} with TTL: {TTL}", key, ttl);

            return Task.CompletedTask;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error setting key: {Key}", key);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<long> IncrementAsync(string key, long increment = 1, TimeSpan? ttl = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(key))
            throw new ArgumentException("Key cannot be null or empty", nameof(key));

        var semaphore = _locks.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));

        await semaphore.WaitAsync(cancellationToken);
        try
        {
            var currentValue = await GetCurrentNumericValueAsync(key);
            var newValue = currentValue + increment;

            await SetAsync(key, newValue.ToString(), ttl, cancellationToken);

            _logger?.LogDebug("Incremented key: {Key} by {Increment}, new value: {NewValue}", key, increment, newValue);
            return newValue;
        }
        finally
        {
            semaphore.Release();
        }
    }

    /// <inheritdoc />
    public async Task<long> DecrementAsync(string key, long decrement = 1, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(key))
            throw new ArgumentException("Key cannot be null or empty", nameof(key));

        var semaphore = _locks.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));

        await semaphore.WaitAsync(cancellationToken);
        try
        {
            var currentValue = await GetCurrentNumericValueAsync(key);
            var newValue = Math.Max(0, currentValue - decrement);

            await SetAsync(key, newValue.ToString(), null, cancellationToken);

            _logger?.LogDebug("Decremented key: {Key} by {Decrement}, new value: {NewValue}", key, decrement, newValue);
            return newValue;
        }
        finally
        {
            semaphore.Release();
        }
    }

    /// <inheritdoc />
    public Task RemoveAsync(string key, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(key))
            throw new ArgumentException("Key cannot be null or empty", nameof(key));

        try
        {
            _cache.Remove(key);
            _locks.TryRemove(key, out var semaphore);
            semaphore?.Dispose();

            _logger?.LogDebug("Removed key: {Key}", key);
            return Task.CompletedTask;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error removing key: {Key}", key);
            throw;
        }
    }

    /// <inheritdoc />
    public Task<bool> ExistsAsync(string key, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(key))
            throw new ArgumentException("Key cannot be null or empty", nameof(key));

        try
        {
            var exists = _cache.TryGetValue(key, out _);
            _logger?.LogDebug("Key exists check: {Key} = {Exists}", key, exists);
            return Task.FromResult(exists);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error checking existence of key: {Key}", key);
            throw;
        }
    }

    /// <inheritdoc />
    public Task ExpireAsync(string key, TimeSpan ttl, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(key))
            throw new ArgumentException("Key cannot be null or empty", nameof(key));

        try
        {
            if (_cache.TryGetValue(key, out var value))
            {
                var options = new MemoryCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = ttl
                };
                _cache.Set(key, value, options);

                _logger?.LogDebug("Set expiration for key: {Key}, TTL: {TTL}", key, ttl);
            }
            else
            {
                _logger?.LogWarning("Cannot set expiration for non-existent key: {Key}", key);
            }

            return Task.CompletedTask;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error setting expiration for key: {Key}", key);
            throw;
        }
    }

    private async Task<long> GetCurrentNumericValueAsync(string key)
    {
        var value = await GetAsync(key);
        if (string.IsNullOrEmpty(value) || !long.TryParse(value, out var numericValue))
        {
            return 0;
        }
        return numericValue;
    }

    /// <summary>
    /// Disposes the storage and cleans up resources.
    /// </summary>
    public void Dispose()
    {
        foreach (var semaphore in _locks.Values)
        {
            semaphore.Dispose();
        }
        _locks.Clear();

        if (_cache is IDisposable disposableCache)
        {
            disposableCache.Dispose();
        }
    }
}
