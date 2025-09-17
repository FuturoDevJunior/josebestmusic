using Microsoft.Extensions.Logging;
using NetThrottler.Core.Interfaces;

namespace NetThrottler.Core.Policies;

/// <summary>
/// Factory for creating rate limiting policies.
/// Supports multiple algorithm types and configuration options.
/// </summary>
public sealed class PolicyFactory : IPolicyFactory
{
    private readonly IThrottleStorage _storage;
    private readonly ILoggerFactory? _loggerFactory;

    /// <summary>
    /// Initializes a new instance of PolicyFactory.
    /// </summary>
    /// <param name="storage">Storage implementation for policies</param>
    /// <param name="loggerFactory">Logger factory (optional)</param>
    public PolicyFactory(IThrottleStorage storage, ILoggerFactory? loggerFactory = null)
    {
        _storage = storage ?? throw new ArgumentNullException(nameof(storage));
        _loggerFactory = loggerFactory;
    }

    /// <inheritdoc />
    public IPolicy CreatePolicy(
        string name,
        string algorithm,
        int maxRequests,
        TimeSpan window,
        IReadOnlyDictionary<string, object>? parameters = null)
    {
        var config = new PolicyConfiguration(name, algorithm, maxRequests, window, parameters);
        return CreatePolicy(config);
    }

    /// <inheritdoc />
    public IPolicy CreatePolicy(PolicyConfiguration config)
    {
        if (config == null)
            throw new ArgumentNullException(nameof(config));

        var logger = _loggerFactory?.CreateLogger($"NetThrottler.Core.Policies.{config.Algorithm}Policy");

        return config.Algorithm.ToLowerInvariant() switch
        {
            "tokenbucket" => new TokenBucketPolicy(config, _storage, logger as ILogger<TokenBucketPolicy> ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<TokenBucketPolicy>.Instance),
            "leakybucket" => new LeakyBucketPolicy(
                config.Name, 
                config.MaxRequests, 
                GetLeakRateFromConfig(config), 
                _storage, 
                logger as ILogger<LeakyBucketPolicy> ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<LeakyBucketPolicy>.Instance),
            "fixedwindow" => new FixedWindowPolicy(
                config.Name, 
                config.MaxRequests, 
                config.Window, 
                _storage, 
                logger as ILogger<FixedWindowPolicy> ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<FixedWindowPolicy>.Instance),
            "slidingwindow" => new SlidingWindowPolicy(
                config.Name, 
                config.MaxRequests, 
                config.Window, 
                _storage, 
                logger as ILogger<SlidingWindowPolicy> ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<SlidingWindowPolicy>.Instance),
            _ => throw new ArgumentException($"Unsupported algorithm: {config.Algorithm}", nameof(config))
        };
    }

    /// <summary>
    /// Creates a Token Bucket policy with the specified parameters.
    /// </summary>
    /// <param name="name">Policy name</param>
    /// <param name="capacity">Bucket capacity</param>
    /// <param name="refillRatePerSecond">Refill rate in tokens per second</param>
    /// <returns>Token Bucket policy</returns>
    public TokenBucketPolicy CreateTokenBucketPolicy(string name, double capacity, double refillRatePerSecond)
    {
        var logger = _loggerFactory?.CreateLogger<TokenBucketPolicy>();
        return new TokenBucketPolicy(name, capacity, refillRatePerSecond, _storage, logger);
    }

    /// <summary>
    /// Creates a policy from a configuration dictionary.
    /// </summary>
    /// <param name="name">Policy name</param>
    /// <param name="config">Configuration dictionary</param>
    /// <returns>Configured policy</returns>
    public IPolicy CreatePolicyFromConfig(string name, IReadOnlyDictionary<string, object> config)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Policy name cannot be null or empty", nameof(name));
        if (config == null)
            throw new ArgumentNullException(nameof(config));

        var algorithm = GetRequiredConfigValue<string>(config, "Algorithm");
        var maxRequests = GetRequiredConfigValue<int>(config, "MaxRequests");
        var windowSeconds = GetRequiredConfigValue<int>(config, "WindowSeconds");
        var window = TimeSpan.FromSeconds(windowSeconds);

        var parameters = config.Where(kvp => !IsStandardConfigKey(kvp.Key))
                              .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);

        return CreatePolicy(name, algorithm, maxRequests, window, parameters);
    }

    private static T GetRequiredConfigValue<T>(IReadOnlyDictionary<string, object> config, string key)
    {
        if (!config.TryGetValue(key, out var value))
            throw new ArgumentException($"Required configuration key '{key}' not found");

        try
        {
            return (T)Convert.ChangeType(value, typeof(T));
        }
        catch (Exception ex)
        {
            throw new ArgumentException($"Invalid value for configuration key '{key}': {value}", ex);
        }
    }

    private static double GetLeakRateFromConfig(PolicyConfiguration config)
    {
        if (config.Parameters?.TryGetValue("LeakRatePerSecond", out var rateObj) == true)
        {
            return Convert.ToDouble(rateObj);
        }
        // Calculate leak rate based on window and max requests
        return config.MaxRequests / config.Window.TotalSeconds;
    }

    private static bool IsStandardConfigKey(string key)
    {
        return key.Equals("Algorithm", StringComparison.OrdinalIgnoreCase) ||
               key.Equals("MaxRequests", StringComparison.OrdinalIgnoreCase) ||
               key.Equals("WindowSeconds", StringComparison.OrdinalIgnoreCase);
    }
}
