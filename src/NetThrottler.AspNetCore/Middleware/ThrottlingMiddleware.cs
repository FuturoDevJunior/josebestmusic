using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NetThrottler.AspNetCore.Options;
using NetThrottler.Core.Interfaces;
using System.Text.Json;

namespace NetThrottler.AspNetCore.Middleware;

/// <summary>
/// Middleware for rate limiting HTTP requests using NetThrottler.
/// </summary>
public class ThrottlingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ThrottlingOptions _options;
    private readonly ILogger<ThrottlingMiddleware> _logger;
    private readonly IRateLimiter _rateLimiter;

    /// <summary>
    /// Initializes a new instance of ThrottlingMiddleware.
    /// </summary>
    /// <param name="next">Next middleware in the pipeline</param>
    /// <param name="options">Throttling options</param>
    /// <param name="logger">Logger instance</param>
    /// <param name="rateLimiter">Rate limiter instance</param>
    public ThrottlingMiddleware(
        RequestDelegate next,
        IOptions<ThrottlingOptions> options,
        ILogger<ThrottlingMiddleware> logger,
        IRateLimiter rateLimiter)
    {
        _next = next ?? throw new ArgumentNullException(nameof(next));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _rateLimiter = rateLimiter ?? throw new ArgumentNullException(nameof(rateLimiter));
    }

    /// <summary>
    /// Invokes the middleware.
    /// </summary>
    /// <param name="context">HTTP context</param>
    /// <returns>Task representing the middleware execution</returns>
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            // Check if rate limiting should be skipped for this request
            if (_options.SkipRateLimiting?.Invoke(context) == true)
            {
                _logger.LogDebug("Skipping rate limiting for request: {Path}", context.Request.Path);
                await _next(context);
                return;
            }

            // Extract the rate limiting key
            var key = _options.KeyResolver(context);
            if (string.IsNullOrEmpty(key))
            {
                _logger.LogWarning("Rate limiting key is null or empty for request: {Path}", context.Request.Path);
                await _next(context);
                return;
            }

            // Check rate limit
            var isAllowed = await _rateLimiter.TryAcquireAsync(key, 1, context.RequestAborted);

            if (!isAllowed)
            {
                _logger.LogWarning("Rate limit exceeded for key: {Key}, path: {Path}", key, context.Request.Path);
                await HandleRateLimitedRequest(context, key);
                return;
            }

            // Get current state for headers
            var state = await _rateLimiter.GetStateAsync(key, context.RequestAborted);
            if (state != null && _options.IncludeRateLimitHeaders)
            {
                AddRateLimitHeaders(context, state);
            }

            _logger.LogDebug("Request allowed for key: {Key}, remaining: {Remaining}",
                key, state?.RemainingPermits ?? 0);

            await _next(context);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in throttling middleware for request: {Path}", context.Request.Path);

            // In case of error, allow the request to proceed
            await _next(context);
        }
    }

    private async Task HandleRateLimitedRequest(HttpContext context, string key)
    {
        // Get current state for headers and retry-after calculation
        var state = await _rateLimiter.GetStateAsync(key, context.RequestAborted);

        // Set response status code
        context.Response.StatusCode = _options.RateLimitStatusCode;

        // Add rate limit headers
        if (_options.IncludeRateLimitHeaders && state != null)
        {
            AddRateLimitHeaders(context, state);

            // Calculate retry-after in seconds
            var retryAfter = Math.Max(1, (int)(state.ResetTime - DateTimeOffset.UtcNow).TotalSeconds);
            context.Response.Headers.Add(_options.RetryAfterHeaderName, retryAfter.ToString());
        }

        // Set content type
        context.Response.ContentType = "application/json";

        // Create error response
        var errorResponse = new
        {
            error = "Rate limit exceeded",
            message = _options.RateLimitMessage,
            retryAfter = state != null ? (int)(state.ResetTime - DateTimeOffset.UtcNow).TotalSeconds : 60
        };

        var json = JsonSerializer.Serialize(errorResponse, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });

        await context.Response.WriteAsync(json);

        // Call custom handler if configured
        if (_options.OnRateLimited != null && state != null)
        {
            try
            {
                await _options.OnRateLimited(context, state);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in custom rate limit handler");
            }
        }
    }

    private void AddRateLimitHeaders(HttpContext context, RateLimitState state)
    {
        if (state == null) return;

        var headers = context.Response.Headers;

        headers[_options.RemainingHeaderName] = state.RemainingPermits.ToString();
        headers[_options.LimitHeaderName] = state.TotalPermits.ToString();
        headers[_options.ResetHeaderName] = ((DateTimeOffset)state.ResetTime).ToUnixTimeSeconds().ToString();
    }
}
