using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NetThrottler.Core.Interfaces;
using NetThrottler.Redis.Options;
using NetThrottler.Redis.Storage;
using StackExchange.Redis;

namespace NetThrottler.Redis.Extensions;

/// <summary>
/// Extension methods for configuring Redis throttle storage services.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds Redis throttle storage to the service collection.
    /// </summary>
    /// <param name="services">Service collection</param>
    /// <param name="configuration">Configuration section</param>
    /// <returns>Service collection for chaining</returns>
    public static IServiceCollection AddRedisThrottleStorage(
        this IServiceCollection services,
        IConfigurationSection configuration)
    {
        if (services == null)
            throw new ArgumentNullException(nameof(services));
        if (configuration == null)
            throw new ArgumentNullException(nameof(configuration));

        // Configure options
        services.Configure<RedisThrottleStorageOptions>(configuration.Bind);

        // Register Redis connection
        services.AddSingleton<IConnectionMultiplexer>(provider =>
        {
            var options = provider.GetRequiredService<IOptions<RedisThrottleStorageOptions>>().Value;
            var configurationOptions = ConfigurationOptions.Parse(options.ConnectionString);
            configurationOptions.DefaultDatabase = options.Database;
            configurationOptions.ConnectTimeout = (int)options.ConnectionTimeout.TotalMilliseconds;
            configurationOptions.SyncTimeout = (int)options.SyncTimeout.TotalMilliseconds;
            configurationOptions.Ssl = options.UseSsl;
            configurationOptions.Password = options.Password;
            configurationOptions.ClientName = options.ClientName;
            configurationOptions.AbortOnConnectFail = options.AbortOnConnectFail;

            return ConnectionMultiplexer.Connect(configurationOptions);
        });

        // Register storage
        services.AddSingleton<IThrottleStorage, RedisThrottleStorage>();

        return services;
    }

    /// <summary>
    /// Adds Redis throttle storage to the service collection with custom configuration.
    /// </summary>
    /// <param name="services">Service collection</param>
    /// <param name="configureOptions">Options configuration action</param>
    /// <returns>Service collection for chaining</returns>
    public static IServiceCollection AddRedisThrottleStorage(
        this IServiceCollection services,
        Action<RedisThrottleStorageOptions> configureOptions)
    {
        if (services == null)
            throw new ArgumentNullException(nameof(services));
        if (configureOptions == null)
            throw new ArgumentNullException(nameof(configureOptions));

        // Configure options
        services.Configure(configureOptions);

        // Register Redis connection
        services.AddSingleton<IConnectionMultiplexer>(provider =>
        {
            var options = provider.GetRequiredService<IOptions<RedisThrottleStorageOptions>>().Value;
            var configurationOptions = ConfigurationOptions.Parse(options.ConnectionString);
            configurationOptions.DefaultDatabase = options.Database;
            configurationOptions.ConnectTimeout = (int)options.ConnectionTimeout.TotalMilliseconds;
            configurationOptions.SyncTimeout = (int)options.SyncTimeout.TotalMilliseconds;
            configurationOptions.Ssl = options.UseSsl;
            configurationOptions.Password = options.Password;
            configurationOptions.ClientName = options.ClientName;
            configurationOptions.AbortOnConnectFail = options.AbortOnConnectFail;

            return ConnectionMultiplexer.Connect(configurationOptions);
        });

        // Register storage
        services.AddSingleton<IThrottleStorage, RedisThrottleStorage>();

        return services;
    }

    /// <summary>
    /// Adds Redis throttle storage with an existing connection multiplexer.
    /// </summary>
    /// <param name="services">Service collection</param>
    /// <param name="connectionMultiplexer">Existing Redis connection</param>
    /// <param name="configureOptions">Options configuration action</param>
    /// <returns>Service collection for chaining</returns>
    public static IServiceCollection AddRedisThrottleStorage(
        this IServiceCollection services,
        IConnectionMultiplexer connectionMultiplexer,
        Action<RedisThrottleStorageOptions>? configureOptions = null)
    {
        if (services == null)
            throw new ArgumentNullException(nameof(services));
        if (connectionMultiplexer == null)
            throw new ArgumentNullException(nameof(connectionMultiplexer));

        // Configure options if provided
        if (configureOptions != null)
        {
            services.Configure(configureOptions);
        }

        // Register existing connection
        services.AddSingleton(connectionMultiplexer);

        // Register storage
        services.AddSingleton<IThrottleStorage>(provider =>
        {
            var options = provider.GetService<IOptions<RedisThrottleStorageOptions>>()?.Value ?? new RedisThrottleStorageOptions();
            return new RedisThrottleStorage(connectionMultiplexer, Microsoft.Extensions.Options.Options.Create(options));
        });

        return services;
    }

    /// <summary>
    /// Adds Redis throttle storage with default configuration.
    /// </summary>
    /// <param name="services">Service collection</param>
    /// <param name="connectionString">Redis connection string</param>
    /// <returns>Service collection for chaining</returns>
    public static IServiceCollection AddRedisThrottleStorage(
        this IServiceCollection services,
        string connectionString)
    {
        if (services == null)
            throw new ArgumentNullException(nameof(services));
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new ArgumentException("Connection string cannot be null or empty", nameof(connectionString));

        return services.AddRedisThrottleStorage(options =>
        {
            options.ConnectionString = connectionString;
        });
    }
}
