namespace NetThrottler.Core.Interfaces;

/// <summary>
/// Core interface for rate limiting functionality.
/// Provides the ability to check if a request should be allowed based on a key.
/// </summary>
public interface IRateLimiter
{
    /// <summary>
    /// Attempts to acquire the specified number of permits for the given key.
    /// </summary>
    /// <param name="key">The unique identifier for the rate limit (e.g., user ID, IP address)</param>
    /// <param name="permits">Number of permits to acquire (default: 1)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if the request is allowed, false if rate limited</returns>
    Task<bool> TryAcquireAsync(string key, int permits = 1, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the current state of the rate limiter for the specified key.
    /// </summary>
    /// <param name="key">The unique identifier</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Rate limit state information</returns>
    Task<RateLimitState?> GetStateAsync(string key, CancellationToken cancellationToken = default);
}

/// <summary>
/// Represents the current state of a rate limiter for a specific key.
/// </summary>
/// <param name="Key">The rate limit key</param>
/// <param name="RemainingPermits">Number of permits remaining</param>
/// <param name="ResetTime">When the rate limit will reset</param>
/// <param name="TotalPermits">Total permits available in the window</param>
public record RateLimitState(string Key, int RemainingPermits, DateTimeOffset ResetTime, int TotalPermits);
