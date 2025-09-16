namespace NetThrottler.Redis.Options;

/// <summary>
/// Configuration options for Redis throttle storage.
/// </summary>
public class RedisThrottleStorageOptions
{
    /// <summary>
    /// Gets or sets the Redis connection string.
    /// </summary>
    public string ConnectionString { get; set; } = "localhost:6379";

    /// <summary>
    /// Gets or sets the Redis database number to use.
    /// </summary>
    public int Database { get; set; } = 0;

    /// <summary>
    /// Gets or sets the key prefix for all throttle-related keys.
    /// </summary>
    public string KeyPrefix { get; set; } = "netthrottler:";

    /// <summary>
    /// Gets or sets the default TTL for keys when no specific TTL is provided.
    /// </summary>
    public TimeSpan DefaultTtl { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Gets or sets the connection timeout.
    /// </summary>
    public TimeSpan ConnectionTimeout { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Gets or sets the sync timeout.
    /// </summary>
    public TimeSpan SyncTimeout { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Gets or sets whether to use SSL for Redis connection.
    /// </summary>
    public bool UseSsl { get; set; } = false;

    /// <summary>
    /// Gets or sets the password for Redis authentication.
    /// </summary>
    public string? Password { get; set; }

    /// <summary>
    /// Gets or sets the client name for Redis connection.
    /// </summary>
    public string? ClientName { get; set; } = "NetThrottler";

    /// <summary>
    /// Gets or sets whether to abort on connection failure.
    /// </summary>
    public bool AbortOnConnectFail { get; set; } = false;

    /// <summary>
    /// Gets or sets the retry policy for failed operations.
    /// </summary>
    public RetryPolicy RetryPolicy { get; set; } = new();

    /// <summary>
    /// Gets or sets additional Redis configuration options.
    /// </summary>
    public Dictionary<string, object> AdditionalOptions { get; set; } = new();
}

/// <summary>
/// Retry policy configuration for Redis operations.
/// </summary>
public class RetryPolicy
{
    /// <summary>
    /// Gets or sets the maximum number of retry attempts.
    /// </summary>
    public int MaxRetries { get; set; } = 3;

    /// <summary>
    /// Gets or sets the base delay between retries.
    /// </summary>
    public TimeSpan BaseDelay { get; set; } = TimeSpan.FromMilliseconds(100);

    /// <summary>
    /// Gets or sets the maximum delay between retries.
    /// </summary>
    public TimeSpan MaxDelay { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Gets or sets whether to use exponential backoff.
    /// </summary>
    public bool UseExponentialBackoff { get; set; } = true;
}
