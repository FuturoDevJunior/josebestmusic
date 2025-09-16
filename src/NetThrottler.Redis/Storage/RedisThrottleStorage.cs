using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NetThrottler.Core.Interfaces;
using NetThrottler.Redis.Options;
using StackExchange.Redis;

namespace NetThrottler.Redis.Storage;

/// <summary>
/// Redis implementation of IThrottleStorage using StackExchange.Redis.
/// Provides distributed, thread-safe storage for rate limiting state.
/// </summary>
public sealed class RedisThrottleStorage : IThrottleStorage, IDisposable
{
    private readonly IDatabase _database;
    private readonly IConnectionMultiplexer _connection;
    private readonly RedisThrottleStorageOptions _options;
    private readonly ILogger<RedisThrottleStorage>? _logger;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of RedisThrottleStorage.
    /// </summary>
    /// <param name="options">Redis configuration options</param>
    /// <param name="logger">Logger instance (optional)</param>
    public RedisThrottleStorage(IOptions<RedisThrottleStorageOptions> options, ILogger<RedisThrottleStorage>? logger = null)
    {
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger;

        try
        {
            var configuration = ConfigurationOptions.Parse(_options.ConnectionString);
            configuration.DefaultDatabase = _options.Database;
            configuration.ConnectTimeout = (int)_options.ConnectionTimeout.TotalMilliseconds;
            configuration.SyncTimeout = (int)_options.SyncTimeout.TotalMilliseconds;
            configuration.Ssl = _options.UseSsl;
            configuration.Password = _options.Password;
            configuration.ClientName = _options.ClientName;
            configuration.AbortOnConnectFail = _options.AbortOnConnectFail;

            _connection = ConnectionMultiplexer.Connect(configuration);
            _database = _connection.GetDatabase(_options.Database);

            _logger?.LogInformation("Connected to Redis at {ConnectionString}", _options.ConnectionString);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to connect to Redis at {ConnectionString}", _options.ConnectionString);
            throw;
        }
    }

    /// <summary>
    /// Initializes a new instance with connection multiplexer.
    /// </summary>
    /// <param name="connection">Redis connection multiplexer</param>
    /// <param name="options">Redis configuration options</param>
    /// <param name="logger">Logger instance (optional)</param>
    public RedisThrottleStorage(
        IConnectionMultiplexer connection,
        IOptions<RedisThrottleStorageOptions> options,
        ILogger<RedisThrottleStorage>? logger = null)
    {
        _connection = connection ?? throw new ArgumentNullException(nameof(connection));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger;
        _database = _connection.GetDatabase(_options.Database);
    }

    /// <inheritdoc />
    public async Task<string?> GetAsync(string key, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(key))
            throw new ArgumentException("Key cannot be null or empty", nameof(key));

        var redisKey = GetRedisKey(key);

        try
        {
            var value = await _database.StringGetAsync(redisKey);
            _logger?.LogDebug("Retrieved value for key: {Key}", key);
            return value.HasValue ? value.ToString() : null;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error retrieving key: {Key}", key);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task SetAsync(string key, string value, TimeSpan? ttl = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(key))
            throw new ArgumentException("Key cannot be null or empty", nameof(key));

        var redisKey = GetRedisKey(key);
        var ttlSeconds = (int)(ttl ?? _options.DefaultTtl).TotalSeconds;

        try
        {
            if (ttlSeconds > 0)
            {
                await _database.StringSetAsync(redisKey, value, TimeSpan.FromSeconds(ttlSeconds));
            }
            else
            {
                await _database.StringSetAsync(redisKey, value);
            }

            _logger?.LogDebug("Set value for key: {Key} with TTL: {TTL}", key, ttl);
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

        var redisKey = GetRedisKey(key);
        var ttlSeconds = (int)(ttl ?? _options.DefaultTtl).TotalSeconds;

        try
        {
            var newValue = await _database.StringIncrementAsync(redisKey, increment);

            if (ttlSeconds > 0)
            {
                await _database.KeyExpireAsync(redisKey, TimeSpan.FromSeconds(ttlSeconds));
            }

            _logger?.LogDebug("Incremented key: {Key} by {Increment}, new value: {NewValue}", key, increment, newValue);
            return newValue;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error incrementing key: {Key}", key);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<long> DecrementAsync(string key, long decrement = 1, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(key))
            throw new ArgumentException("Key cannot be null or empty", nameof(key));

        var redisKey = GetRedisKey(key);

        try
        {
            var newValue = await _database.StringDecrementAsync(redisKey, decrement);

            // Ensure value doesn't go below 0
            if (newValue < 0)
            {
                await _database.StringSetAsync(redisKey, 0);
                newValue = 0;
            }

            _logger?.LogDebug("Decremented key: {Key} by {Decrement}, new value: {NewValue}", key, decrement, newValue);
            return newValue;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error decrementing key: {Key}", key);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task RemoveAsync(string key, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(key))
            throw new ArgumentException("Key cannot be null or empty", nameof(key));

        var redisKey = GetRedisKey(key);

        try
        {
            await _database.KeyDeleteAsync(redisKey);
            _logger?.LogDebug("Removed key: {Key}", key);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error removing key: {Key}", key);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<bool> ExistsAsync(string key, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(key))
            throw new ArgumentException("Key cannot be null or empty", nameof(key));

        var redisKey = GetRedisKey(key);

        try
        {
            var exists = await _database.KeyExistsAsync(redisKey);
            _logger?.LogDebug("Key exists check: {Key} = {Exists}", key, exists);
            return exists;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error checking existence of key: {Key}", key);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task ExpireAsync(string key, TimeSpan ttl, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(key))
            throw new ArgumentException("Key cannot be null or empty", nameof(key));

        var redisKey = GetRedisKey(key);

        try
        {
            var exists = await _database.KeyExistsAsync(redisKey);
            if (exists)
            {
                await _database.KeyExpireAsync(redisKey, ttl);
                _logger?.LogDebug("Set expiration for key: {Key}, TTL: {TTL}", key, ttl);
            }
            else
            {
                _logger?.LogWarning("Cannot set expiration for non-existent key: {Key}", key);
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error setting expiration for key: {Key}", key);
            throw;
        }
    }

    private string GetRedisKey(string key) => $"{_options.KeyPrefix}{key}";

    /// <summary>
    /// Disposes the storage and cleans up resources.
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;

        _connection?.Dispose();
        _disposed = true;
    }
}
