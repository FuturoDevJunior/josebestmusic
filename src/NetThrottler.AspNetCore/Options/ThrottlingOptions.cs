using Microsoft.AspNetCore.Http;
using NetThrottler.Core.Interfaces;

namespace NetThrottler.AspNetCore.Options;

/// <summary>
/// Configuration options for NetThrottler middleware.
/// </summary>
public class ThrottlingOptions
{
    /// <summary>
    /// Gets or sets the default policy to use when no specific policy is configured.
    /// </summary>
    public string DefaultPolicy { get; set; } = "Default";

    /// <summary>
    /// Gets or sets the key resolver function to extract rate limiting keys from HTTP requests.
    /// </summary>
    public Func<HttpContext, string> KeyResolver { get; set; } = DefaultKeyResolver;

    /// <summary>
    /// Gets or sets the response message when rate limited.
    /// </summary>
    public string RateLimitMessage { get; set; } = "Rate limit exceeded. Please try again later.";

    /// <summary>
    /// Gets or sets the HTTP status code to return when rate limited.
    /// </summary>
    public int RateLimitStatusCode { get; set; } = 429;

    /// <summary>
    /// Gets or sets whether to include rate limit headers in the response.
    /// </summary>
    public bool IncludeRateLimitHeaders { get; set; } = true;

    /// <summary>
    /// Gets or sets the header name for remaining requests.
    /// </summary>
    public string RemainingHeaderName { get; set; } = "X-RateLimit-Remaining";

    /// <summary>
    /// Gets or sets the header name for reset time.
    /// </summary>
    public string ResetHeaderName { get; set; } = "X-RateLimit-Reset";

    /// <summary>
    /// Gets or sets the header name for retry after.
    /// </summary>
    public string RetryAfterHeaderName { get; set; } = "Retry-After";

    /// <summary>
    /// Gets or sets the header name for limit.
    /// </summary>
    public string LimitHeaderName { get; set; } = "X-RateLimit-Limit";

    /// <summary>
    /// Gets or sets the policies configuration.
    /// </summary>
    public Dictionary<string, ThrottlingPolicyConfiguration> Policies { get; set; } = new();

    /// <summary>
    /// Gets or sets the storage configuration.
    /// </summary>
    public StorageConfiguration Storage { get; set; } = new();

    /// <summary>
    /// Gets or sets whether to skip rate limiting for certain requests.
    /// </summary>
    public Func<HttpContext, bool>? SkipRateLimiting { get; set; }

    /// <summary>
    /// Gets or sets the custom error response handler.
    /// </summary>
    public Func<HttpContext, RateLimitState, Task>? OnRateLimited { get; set; }

    /// <summary>
    /// Default key resolver that uses the client IP address.
    /// </summary>
    /// <param name="context">HTTP context</param>
    /// <returns>Rate limiting key</returns>
    public static string DefaultKeyResolver(HttpContext context)
    {
        // Try to get the real IP address from headers (for reverse proxy scenarios)
        var forwardedFor = context.Request.Headers["X-Forwarded-For"].FirstOrDefault();
        if (!string.IsNullOrEmpty(forwardedFor))
        {
            return forwardedFor.Split(',')[0].Trim();
        }

        var realIp = context.Request.Headers["X-Real-IP"].FirstOrDefault();
        if (!string.IsNullOrEmpty(realIp))
        {
            return realIp;
        }

        // Fall back to connection remote IP
        return context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    }

    /// <summary>
    /// Key resolver that uses the authenticated user ID.
    /// </summary>
    /// <param name="context">HTTP context</param>
    /// <returns>Rate limiting key</returns>
    public static string UserIdKeyResolver(HttpContext context)
    {
        var userId = context.User?.Identity?.Name;
        if (string.IsNullOrEmpty(userId))
        {
            // Fall back to IP if user is not authenticated
            return DefaultKeyResolver(context);
        }
        return $"user:{userId}";
    }

    /// <summary>
    /// Key resolver that combines user ID and IP address.
    /// </summary>
    /// <param name="context">HTTP context</param>
    /// <returns>Rate limiting key</returns>
    public static string UserAndIpKeyResolver(HttpContext context)
    {
        var userId = context.User?.Identity?.Name;
        var ip = DefaultKeyResolver(context);

        if (string.IsNullOrEmpty(userId))
        {
            return $"ip:{ip}";
        }

        return $"user:{userId}:ip:{ip}";
    }
}

/// <summary>
/// Configuration for a specific rate limiting policy.
/// </summary>
public class ThrottlingPolicyConfiguration
{
    /// <summary>
    /// Gets or sets the algorithm type.
    /// </summary>
    public string Algorithm { get; set; } = "TokenBucket";

    /// <summary>
    /// Gets or sets the maximum number of requests per window.
    /// </summary>
    public int MaxRequests { get; set; } = 100;

    /// <summary>
    /// Gets or sets the time window in seconds.
    /// </summary>
    public int WindowSeconds { get; set; } = 60;

    /// <summary>
    /// Gets or sets additional algorithm-specific parameters.
    /// </summary>
    public Dictionary<string, object> Parameters { get; set; } = new();

    /// <summary>
    /// Gets or sets whether this policy is enabled.
    /// </summary>
    public bool Enabled { get; set; } = true;
}

/// <summary>
/// Configuration for storage backends.
/// </summary>
public class StorageConfiguration
{
    /// <summary>
    /// Gets or sets the storage type.
    /// </summary>
    public string Type { get; set; } = "Memory";

    /// <summary>
    /// Gets or sets the connection string for distributed storage.
    /// </summary>
    public string? ConnectionString { get; set; }

    /// <summary>
    /// Gets or sets additional storage-specific parameters.
    /// </summary>
    public Dictionary<string, object> Parameters { get; set; } = new();
}
