using Microsoft.Extensions.Logging;
using NetThrottler.Core.Interfaces;
using Polly;
using Polly.CircuitBreaker;
using Polly.Retry;
using Polly.Timeout;

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
    public ResiliencePipeline<HttpResponseMessage> CreateComprehensiveStrategy(
        int retryCount = 3,
        int circuitBreakerThreshold = 5,
        TimeSpan? circuitBreakerDuration = null)
    {
        var duration = circuitBreakerDuration ?? TimeSpan.FromSeconds(30);

        return new ResiliencePipelineBuilder<HttpResponseMessage>()
            .AddRetry(new RetryStrategyOptions<HttpResponseMessage>
            {
                MaxRetryAttempts = retryCount,
                BackoffType = DelayBackoffType.Exponential,
                UseJitter = true,
                Delay = TimeSpan.FromSeconds(1),
                ShouldHandle = new PredicateBuilder<HttpResponseMessage>()
                    .Handle<HttpRequestException>()
                    .HandleResult(r => !r.IsSuccessStatusCode),
                OnRetry = args =>
                {
                    _logger.LogWarning("Retry {RetryCount} after {Delay}ms due to: {Reason}",
                        args.AttemptNumber, args.RetryDelay.TotalMilliseconds, "HTTP Error");
                    return ValueTask.CompletedTask;
                }
            })
            .AddCircuitBreaker(new CircuitBreakerStrategyOptions<HttpResponseMessage>
            {
                FailureRatio = 0.5,
                SamplingDuration = TimeSpan.FromSeconds(10),
                MinimumThroughput = circuitBreakerThreshold,
                BreakDuration = duration,
                ShouldHandle = new PredicateBuilder<HttpResponseMessage>()
                    .Handle<HttpRequestException>()
                    .HandleResult(r => !r.IsSuccessStatusCode),
                OnOpened = args =>
                {
                    _logger.LogWarning("Circuit breaker opened for {Duration}ms", duration.TotalMilliseconds);
                    return ValueTask.CompletedTask;
                },
                OnClosed = args =>
                {
                    _logger.LogInformation("Circuit breaker reset");
                    return ValueTask.CompletedTask;
                }
            })
            .Build();
    }

    /// <summary>
    /// Creates a retry strategy with exponential backoff.
    /// </summary>
    /// <param name="retryCount">The number of retry attempts.</param>
    /// <returns>A retry policy.</returns>
    public ResiliencePipeline<HttpResponseMessage> CreateRetryStrategy(int retryCount = 3)
    {
        return new ResiliencePipelineBuilder<HttpResponseMessage>()
            .AddRetry(new RetryStrategyOptions<HttpResponseMessage>
            {
                MaxRetryAttempts = retryCount,
                BackoffType = DelayBackoffType.Exponential,
                UseJitter = true,
                Delay = TimeSpan.FromSeconds(1),
                ShouldHandle = new PredicateBuilder<HttpResponseMessage>()
                    .Handle<HttpRequestException>()
                    .HandleResult(r => !r.IsSuccessStatusCode),
                OnRetry = args =>
                {
                    _logger.LogWarning("Retry {RetryCount} after {Delay}ms due to: {Reason}",
                        args.AttemptNumber, args.RetryDelay.TotalMilliseconds, "HTTP Error");
                    return ValueTask.CompletedTask;
                }
            })
            .Build();
    }

    /// <summary>
    /// Creates a circuit breaker strategy.
    /// </summary>
    /// <param name="threshold">The failure threshold.</param>
    /// <param name="duration">The circuit breaker duration.</param>
    /// <returns>A circuit breaker policy.</returns>
    public ResiliencePipeline<HttpResponseMessage> CreateCircuitBreakerStrategy(
        int threshold = 5,
        TimeSpan? duration = null)
    {
        var circuitDuration = duration ?? TimeSpan.FromSeconds(30);

        return new ResiliencePipelineBuilder<HttpResponseMessage>()
            .AddCircuitBreaker(new CircuitBreakerStrategyOptions<HttpResponseMessage>
            {
                FailureRatio = 0.5,
                SamplingDuration = TimeSpan.FromSeconds(10),
                MinimumThroughput = threshold,
                BreakDuration = circuitDuration,
                ShouldHandle = new PredicateBuilder<HttpResponseMessage>()
                    .Handle<HttpRequestException>()
                    .HandleResult(r => !r.IsSuccessStatusCode),
                OnOpened = args =>
                {
                    _logger.LogWarning("Circuit breaker opened for {Duration}ms", circuitDuration.TotalMilliseconds);
                    return ValueTask.CompletedTask;
                },
                OnClosed = args =>
                {
                    _logger.LogInformation("Circuit breaker reset");
                    return ValueTask.CompletedTask;
                }
            })
            .Build();
    }

    /// <summary>
    /// Creates a timeout strategy.
    /// </summary>
    /// <param name="timeout">The timeout duration.</param>
    /// <returns>A timeout policy.</returns>
    public ResiliencePipeline<HttpResponseMessage> CreateTimeoutStrategy(TimeSpan timeout)
    {
        return new ResiliencePipelineBuilder<HttpResponseMessage>()
            .AddTimeout(new TimeoutStrategyOptions
            {
                Timeout = timeout,
                OnTimeout = args =>
                {
                    _logger.LogWarning("Request timed out after {Timeout}ms", timeout.TotalMilliseconds);
                    return ValueTask.CompletedTask;
                }
            })
            .Build();
    }
}
