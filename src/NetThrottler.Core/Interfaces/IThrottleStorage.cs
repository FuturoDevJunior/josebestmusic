namespace NetThrottler.Core.Interfaces;

/// <summary>
/// Storage abstraction for rate limiting state and counters.
/// Provides thread-safe operations for distributed rate limiting scenarios.
/// </summary>
public interface IThrottleStorage
{
    /// <summary>
    /// Gets a stored value for the specified key.
    /// </summary>
    /// <param name="key">The storage key</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The stored value or null if not found</returns>
    Task<string?> GetAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sets a value for the specified key with optional expiration.
    /// </summary>
    /// <param name="key">The storage key</param>
    /// <param name="value">The value to store</param>
    /// <param name="ttl">Time to live (null = no expiration)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task SetAsync(string key, string value, TimeSpan? ttl = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Increments a numeric value atomically and returns the new value.
    /// </summary>
    /// <param name="key">The storage key</param>
    /// <param name="increment">Amount to increment by (default: 1)</param>
    /// <param name="ttl">Time to live for the key (null = no expiration)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The new value after increment</returns>
    Task<long> IncrementAsync(string key, long increment = 1, TimeSpan? ttl = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Decrements a numeric value atomically and returns the new value.
    /// </summary>
    /// <param name="key">The storage key</param>
    /// <param name="decrement">Amount to decrement by (default: 1)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The new value after decrement</returns>
    Task<long> DecrementAsync(string key, long decrement = 1, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes a key from storage.
    /// </summary>
    /// <param name="key">The storage key</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task RemoveAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks if a key exists in storage.
    /// </summary>
    /// <param name="key">The storage key</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if the key exists</returns>
    Task<bool> ExistsAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sets an expiration time for an existing key.
    /// </summary>
    /// <param name="key">The storage key</param>
    /// <param name="ttl">Time to live</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task ExpireAsync(string key, TimeSpan ttl, CancellationToken cancellationToken = default);
}
