using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NetThrottler.Core.Interfaces;
using NetThrottler.Core.Storage;
using NetThrottler.HttpClient.Handlers;
using NetThrottler.HttpClient.Options;
using System.Net.Http;

namespace NetThrottler.HttpClient.Extensions;

/// <summary>
/// Extension methods for adding HttpClient throttling services.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds HttpClient throttling services to the specified <see cref="IServiceCollection"/>.
    /// </summary>
    /// <param name="services">The <see cref="IServiceCollection"/> to add services to.</param>
    /// <param name="configuration">The configuration section for HttpClient throttling options.</param>
    /// <returns>The <see cref="IServiceCollection"/> so that additional calls can be chained.</returns>
    public static IServiceCollection AddHttpClientThrottling(this IServiceCollection services, IConfigurationSection configuration)
    {
        if (services == null)
            throw new ArgumentNullException(nameof(services));
        if (configuration == null)
            throw new ArgumentNullException(nameof(configuration));

        // Configure options
        services.Configure<HttpClientThrottlingOptions>(configuration.Bind);

        // Register default storage if not already registered
        if (!services.Any(s => s.ServiceType == typeof(IThrottleStorage)))
        {
            services.AddSingleton<IThrottleStorage, MemoryThrottleStorage>();
        }

        // Register the throttling handler
        services.AddTransient<ThrottlingDelegatingHandler>();

        return services;
    }

    /// <summary>
    /// Adds HttpClient throttling services to the specified <see cref="IServiceCollection"/>.
    /// </summary>
    /// <param name="services">The <see cref="IServiceCollection"/> to add services to.</param>
    /// <param name="configureOptions">An action to configure the <see cref="HttpClientThrottlingOptions"/>.</param>
    /// <returns>The <see cref="IServiceCollection"/> so that additional calls can be chained.</returns>
    public static IServiceCollection AddHttpClientThrottling(this IServiceCollection services, Action<HttpClientThrottlingOptions> configureOptions)
    {
        if (services == null)
            throw new ArgumentNullException(nameof(services));
        if (configureOptions == null)
            throw new ArgumentNullException(nameof(configureOptions));

        // Configure options
        services.Configure(configureOptions);

        // Register default storage if not already registered
        if (!services.Any(s => s.ServiceType == typeof(IThrottleStorage)))
        {
            services.AddSingleton<IThrottleStorage, MemoryThrottleStorage>();
        }

        // Register the throttling handler
        services.AddTransient<ThrottlingDelegatingHandler>();

        return services;
    }

    /// <summary>
    /// Adds a throttled HttpClient to the specified <see cref="IServiceCollection"/>.
    /// </summary>
    /// <param name="services">The <see cref="IServiceCollection"/> to add services to.</param>
    /// <param name="name">The name of the HttpClient.</param>
    /// <param name="configureClient">An action to configure the HttpClient.</param>
    /// <returns>The <see cref="IHttpClientBuilder"/> for chaining.</returns>
    public static IHttpClientBuilder AddThrottledHttpClient(this IServiceCollection services, string name, Action<HttpClient>? configureClient = null)
    {
        if (services == null)
            throw new ArgumentNullException(nameof(services));
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("HttpClient name cannot be null or empty.", nameof(name));

        return services.AddHttpClient(name, client =>
        {
            configureClient?.Invoke(client);
        }).AddHttpMessageHandler<ThrottlingDelegatingHandler>();
    }

    /// <summary>
    /// Adds a throttled HttpClient to the specified <see cref="IServiceCollection"/>.
    /// </summary>
    /// <param name="services">The <see cref="IServiceCollection"/> to add services to.</param>
    /// <param name="name">The name of the HttpClient.</param>
    /// <param name="configureClient">An action to configure the HttpClient.</param>
    /// <param name="configureHandler">An action to configure the throttling handler.</param>
    /// <returns>The <see cref="IHttpClientBuilder"/> for chaining.</returns>
    public static IHttpClientBuilder AddThrottledHttpClient(this IServiceCollection services, string name, Action<HttpClient>? configureClient, Action<ThrottlingDelegatingHandler>? configureHandler)
    {
        if (services == null)
            throw new ArgumentNullException(nameof(services));
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("HttpClient name cannot be null or empty.", nameof(name));

        return services.AddHttpClient(name, client =>
        {
            configureClient?.Invoke(client);
        }).AddHttpMessageHandler(provider =>
        {
            var handler = provider.GetRequiredService<ThrottlingDelegatingHandler>();
            configureHandler?.Invoke(handler);
            return handler;
        });
    }

    /// <summary>
    /// Adds a throttled HttpClient with typed client to the specified <see cref="IServiceCollection"/>.
    /// </summary>
    /// <typeparam name="TClient">The type of the typed client.</typeparam>
    /// <typeparam name="TImplementation">The implementation type of the typed client.</typeparam>
    /// <param name="services">The <see cref="IServiceCollection"/> to add services to.</param>
    /// <param name="configureClient">An action to configure the HttpClient.</param>
    /// <returns>The <see cref="IHttpClientBuilder"/> for chaining.</returns>
    public static IHttpClientBuilder AddThrottledHttpClient<TClient, TImplementation>(this IServiceCollection services, Action<HttpClient>? configureClient = null)
        where TClient : class
        where TImplementation : class, TClient
    {
        if (services == null)
            throw new ArgumentNullException(nameof(services));

        return services.AddHttpClient<TClient, TImplementation>(client =>
        {
            configureClient?.Invoke(client);
        }).AddHttpMessageHandler<ThrottlingDelegatingHandler>();
    }
}
