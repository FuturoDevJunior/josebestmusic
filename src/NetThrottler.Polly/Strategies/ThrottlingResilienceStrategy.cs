using Microsoft.Extensions.Logging;
using NetThrottler.Core.Interfaces;
using Polly;
using Polly.Extensions.Http;
using Polly.CircuitBreaker;
using Polly.Retry;

namespace NetThrottler.Polly.Strategies;

/// <summary>
/// A resilience strategy that combines rate limiting with Polly resilience patterns.
/// </summary>
public class ThrottlingResilienceStrategy
{
    private readonly IThrottleStorage _storage;
    private readonly ILogger<ThrottlingResilienceStrategy> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="ThrottlingResilienceStrategy"/> class.
    /// </summary>
    /// <param name="storage">The throttle storage instance.</param>
    /// <param name="logger">The logger instance.</param>
    public ThrottlingResilienceStrategy(IThrottleStorage storage, ILogger<ThrottlingResilienceStrategy> logger)
    {
        _storage = storage ?? throw new ArgumentNullException(nameof(storage));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Creates a comprehensive resilience strategy that includes retry and circuit breaker patterns.
    /// </summary>
    /// <param name="retryCount">The number of retry attempts.</param>
    /// <param name="circuitBreakerThreshold">The circuit breaker failure threshold.</param>
    /// <param name="circuitBreakerDuration">The circuit breaker duration.</param>
    /// <returns>A resilience strategy builder.</returns>
    public IAsyncPolicy<HttpResponseMessage> CreateComprehensiveStrategy(
        int retryCount = 3,
        int circuitBreakerThreshold = 5,
        TimeSpan? circuitBreakerDuration = null)
    {
        var duration = circuitBreakerDuration ?? TimeSpan.FromSeconds(30);

        var retryPolicy = Policy
            .HandleResult<HttpResponseMessage>(r => !r.IsSuccessStatusCode)
            .WaitAndRetryAsync(
                retryCount,
                retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)),
                onRetry: (outcome, timespan, retryCount, context) =>
                {
                    _logger.LogWarning("Retry {RetryCount} after {Delay}ms due to: {Reason}",
                        retryCount, timespan.TotalMilliseconds, outcome.Result?.ReasonPhrase);
                });

        var circuitBreakerPolicy = Policy
            .HandleResult<HttpResponseMessage>(r => !r.IsSuccessStatusCode)
            .CircuitBreakerAsync(
                circuitBreakerThreshold,
                duration,
                onBreak: (result, duration) =>
                {
                    _logger.LogWarning("Circuit breaker opened for {Duration}ms due to: {Reason}",
                        duration.TotalMilliseconds, result.Result?.ReasonPhrase);
                },
                onReset: () =>
                {
                    _logger.LogInformation("Circuit breaker reset");
                });

        return retryPolicy.WrapAsync(circuitBreakerPolicy);
    }

    /// <summary>
    /// Creates a retry strategy with exponential backoff.
    /// </summary>
    /// <param name="retryCount">The number of retry attempts.</param>
    /// <returns>A retry policy.</returns>
    public IAsyncPolicy<HttpResponseMessage> CreateRetryStrategy(int retryCount = 3)
    {
        return Policy
            .HandleResult<HttpResponseMessage>(r => !r.IsSuccessStatusCode)
            .WaitAndRetryAsync(
                retryCount,
                retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)),
                onRetry: (outcome, timespan, retryCount, context) =>
                {
                    _logger.LogWarning("Retry {RetryCount} after {Delay}ms due to: {Reason}",
                        retryCount, timespan.TotalMilliseconds, outcome.Result?.ReasonPhrase);
                });
    }

    /// <summary>
    /// Creates a circuit breaker strategy.
    /// </summary>
    /// <param name="threshold">The failure threshold.</param>
    /// <param name="duration">The circuit breaker duration.</param>
    /// <returns>A circuit breaker policy.</returns>
    public IAsyncPolicy<HttpResponseMessage> CreateCircuitBreakerStrategy(
        int threshold = 5,
        TimeSpan? duration = null)
    {
        var circuitDuration = duration ?? TimeSpan.FromSeconds(30);

        return Policy
            .HandleResult<HttpResponseMessage>(r => !r.IsSuccessStatusCode)
            .CircuitBreakerAsync(
                threshold,
                circuitDuration,
                onBreak: (result, duration) =>
                {
                    _logger.LogWarning("Circuit breaker opened for {Duration}ms due to: {Reason}",
                        duration.TotalMilliseconds, result.Result?.ReasonPhrase);
                },
                onReset: () =>
                {
                    _logger.LogInformation("Circuit breaker reset");
                });
    }

    /// <summary>
    /// Creates a timeout strategy.
    /// </summary>
    /// <param name="timeout">The timeout duration.</param>
    /// <returns>A timeout policy.</returns>
    public IAsyncPolicy<HttpResponseMessage> CreateTimeoutStrategy(TimeSpan timeout)
    {
        return Policy.TimeoutAsync<HttpResponseMessage>(timeout);
    }
}
