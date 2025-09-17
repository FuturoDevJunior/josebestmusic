using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NetThrottler.AspNetCore.Middleware;
using NetThrottler.AspNetCore.Options;
using NetThrottler.Core.Interfaces;
using NetThrottler.Core.Policies;
using NetThrottler.Core.Storage;

namespace NetThrottler.AspNetCore.Extensions;

/// <summary>
/// Extension methods for configuring NetThrottler services.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds NetThrottler services to the service collection.
    /// </summary>
    /// <param name="services">Service collection</param>
    /// <param name="configuration">Configuration section</param>
    /// <returns>Service collection for chaining</returns>
    public static IServiceCollection AddNetThrottler(
        this IServiceCollection services,
        IConfigurationSection configuration)
    {
        if (services == null)
            throw new ArgumentNullException(nameof(services));
        if (configuration == null)
            throw new ArgumentNullException(nameof(configuration));

        // Configure options
        services.Configure<ThrottlingOptions>(configuration);

        // Register storage
        services.AddSingleton<IThrottleStorage>(provider =>
        {
            var options = provider.GetRequiredService<IOptions<ThrottlingOptions>>().Value;
            return CreateStorage(options.Storage, provider);
        });

        // Register policy factory
        services.AddSingleton<IPolicyFactory>(provider =>
        {
            var storage = provider.GetRequiredService<IThrottleStorage>();
            var loggerFactory = provider.GetService<ILoggerFactory>();
            return new PolicyFactory(storage, loggerFactory);
        });

        // Register rate limiter
        services.AddSingleton<IRateLimiter>(provider =>
        {
            var options = provider.GetRequiredService<IOptions<ThrottlingOptions>>().Value;
            var factory = provider.GetRequiredService<IPolicyFactory>();
            var logger = provider.GetRequiredService<ILogger<ThrottlingMiddleware>>();

            // Create the default policy
            var defaultPolicyConfig = options.Policies.GetValueOrDefault(options.DefaultPolicy);
            if (defaultPolicyConfig == null)
            {
                throw new InvalidOperationException($"Default policy '{options.DefaultPolicy}' not found in configuration.");
            }

            var policy = factory.CreatePolicy(new Core.Interfaces.PolicyConfiguration(
                options.DefaultPolicy,
                defaultPolicyConfig.Algorithm,
                defaultPolicyConfig.MaxRequests,
                TimeSpan.FromSeconds(defaultPolicyConfig.WindowSeconds),
                defaultPolicyConfig.Parameters));

            if (policy is not IRateLimiter rateLimiter)
            {
                throw new InvalidOperationException($"Policy '{options.DefaultPolicy}' does not implement IRateLimiter.");
            }

            return rateLimiter;
        });

        return services;
    }

    /// <summary>
    /// Adds NetThrottler services to the service collection with custom configuration.
    /// </summary>
    /// <param name="services">Service collection</param>
    /// <param name="configureOptions">Options configuration action</param>
    /// <returns>Service collection for chaining</returns>
    public static IServiceCollection AddNetThrottler(
        this IServiceCollection services,
        Action<ThrottlingOptions> configureOptions)
    {
        if (services == null)
            throw new ArgumentNullException(nameof(services));
        if (configureOptions == null)
            throw new ArgumentNullException(nameof(configureOptions));

        // Configure options
        services.Configure(configureOptions);

        // Register storage
        services.AddSingleton<IThrottleStorage>(provider =>
        {
            var options = provider.GetRequiredService<IOptions<ThrottlingOptions>>().Value;
            return CreateStorage(options.Storage, provider);
        });

        // Register policy factory
        services.AddSingleton<IPolicyFactory>(provider =>
        {
            var storage = provider.GetRequiredService<IThrottleStorage>();
            var loggerFactory = provider.GetService<ILoggerFactory>();
            return new PolicyFactory(storage, loggerFactory);
        });

        // Register rate limiter
        services.AddSingleton<IRateLimiter>(provider =>
        {
            var options = provider.GetRequiredService<IOptions<ThrottlingOptions>>().Value;
            var factory = provider.GetRequiredService<IPolicyFactory>();
            var logger = provider.GetRequiredService<ILogger<ThrottlingMiddleware>>();

            // Create the default policy
            var defaultPolicyConfig = options.Policies.GetValueOrDefault(options.DefaultPolicy);
            if (defaultPolicyConfig == null)
            {
                throw new InvalidOperationException($"Default policy '{options.DefaultPolicy}' not found in configuration.");
            }

            var policy = factory.CreatePolicy(new Core.Interfaces.PolicyConfiguration(
                options.DefaultPolicy,
                defaultPolicyConfig.Algorithm,
                defaultPolicyConfig.MaxRequests,
                TimeSpan.FromSeconds(defaultPolicyConfig.WindowSeconds),
                defaultPolicyConfig.Parameters));

            if (policy is not IRateLimiter rateLimiter)
            {
                throw new InvalidOperationException($"Policy '{options.DefaultPolicy}' does not implement IRateLimiter.");
            }

            return rateLimiter;
        });

        return services;
    }

    /// <summary>
    /// Adds NetThrottler services with default configuration.
    /// </summary>
    /// <param name="services">Service collection</param>
    /// <returns>Service collection for chaining</returns>
    public static IServiceCollection AddNetThrottler(this IServiceCollection services)
    {
        return services.AddNetThrottler(options =>
        {
            // Default configuration
            options.DefaultPolicy = "Default";
            options.Policies["Default"] = new ThrottlingPolicyConfiguration
            {
                Algorithm = "TokenBucket",
                MaxRequests = 100,
                WindowSeconds = 60,
                Parameters = new Dictionary<string, object>
                {
                    ["Capacity"] = 100.0,
                    ["RefillRatePerSecond"] = 1.67 // 100 requests per 60 seconds
                }
            };
            options.Storage.Type = "Memory";
        });
    }

    private static IThrottleStorage CreateStorage(StorageConfiguration config, IServiceProvider provider)
    {
        return config.Type.ToLowerInvariant() switch
        {
            "memory" => new MemoryThrottleStorage(
                provider.GetService<Microsoft.Extensions.Caching.Memory.IMemoryCache>(),
                provider.GetService<ILogger<MemoryThrottleStorage>>()),
            "redis" => provider.GetService<IThrottleStorage>() 
                ?? throw new InvalidOperationException("Redis storage not configured. Please call AddRedisThrottleStorage() first."),
            "sql" => throw new NotImplementedException("SQL storage not yet implemented"),
            _ => throw new ArgumentException($"Unsupported storage type: {config.Type}")
        };
    }
}

/// <summary>
/// Extension methods for configuring NetThrottler middleware.
/// </summary>
public static class ApplicationBuilderExtensions
{
    /// <summary>
    /// Adds NetThrottler middleware to the application pipeline.
    /// </summary>
    /// <param name="app">Application builder</param>
    /// <returns>Application builder for chaining</returns>
    public static IApplicationBuilder UseNetThrottler(this IApplicationBuilder app)
    {
        if (app == null)
            throw new ArgumentNullException(nameof(app));

        return app.UseMiddleware<ThrottlingMiddleware>();
    }
}
