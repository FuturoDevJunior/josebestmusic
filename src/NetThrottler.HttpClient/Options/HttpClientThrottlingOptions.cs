namespace NetThrottler.HttpClient.Options;

/// <summary>
/// Configuration options for HttpClient throttling.
/// </summary>
public class HttpClientThrottlingOptions
{
    /// <summary>
    /// Gets or sets the default throttling policy to use.
    /// </summary>
    public string DefaultPolicy { get; set; } = "default";

    /// <summary>
    /// Gets or sets the throttling policies configuration.
    /// </summary>
    public Dictionary<string, ThrottlingPolicyConfiguration> Policies { get; set; } = new();

    /// <summary>
    /// Gets or sets whether to throw exceptions when rate limited.
    /// If false, requests will be queued and retried automatically.
    /// </summary>
    public bool ThrowOnRateLimit { get; set; } = false;

    /// <summary>
    /// Gets or sets the maximum retry attempts when rate limited.
    /// </summary>
    public int MaxRetryAttempts { get; set; } = 3;

    /// <summary>
    /// Gets or sets the base delay between retry attempts.
    /// </summary>
    public TimeSpan RetryDelay { get; set; } = TimeSpan.FromMilliseconds(100);

    /// <summary>
    /// Gets or sets the maximum delay between retry attempts.
    /// </summary>
    public TimeSpan MaxRetryDelay { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Gets or sets whether to use exponential backoff for retries.
    /// </summary>
    public bool UseExponentialBackoff { get; set; } = true;

    /// <summary>
    /// Gets or sets the key resolver strategy for rate limiting.
    /// </summary>
    public ThrottlingKeyResolver KeyResolver { get; set; } = ThrottlingKeyResolver.Host;
}

/// <summary>
/// Configuration for a specific throttling policy.
/// </summary>
public class ThrottlingPolicyConfiguration
{
    /// <summary>
    /// Gets or sets the algorithm to use for throttling.
    /// </summary>
    public string Algorithm { get; set; } = "TokenBucket";

    /// <summary>
    /// Gets or sets the capacity for the throttling algorithm.
    /// </summary>
    public int Capacity { get; set; } = 100;

    /// <summary>
    /// Gets or sets the refill rate per second.
    /// </summary>
    public double RefillRatePerSecond { get; set; } = 10.0;

    /// <summary>
    /// Gets or sets the window size for window-based algorithms.
    /// </summary>
    public TimeSpan WindowSize { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>
    /// Gets or sets the burst capacity for burst scenarios.
    /// </summary>
    public int BurstCapacity { get; set; } = 50;
}

/// <summary>
/// Key resolver strategies for rate limiting.
/// </summary>
public enum ThrottlingKeyResolver
{
    /// <summary>
    /// Use the request host as the key.
    /// </summary>
    Host,

    /// <summary>
    /// Use the full request URI as the key.
    /// </summary>
    Uri,

    /// <summary>
    /// Use a custom key resolver function.
    /// </summary>
    Custom
}
