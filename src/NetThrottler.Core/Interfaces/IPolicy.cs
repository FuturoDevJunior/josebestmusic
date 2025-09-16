namespace NetThrottler.Core.Interfaces;

/// <summary>
/// Represents a rate limiting policy configuration.
/// Policies define the rules and parameters for rate limiting.
/// </summary>
public interface IPolicy
{
    /// <summary>
    /// Gets the unique name of this policy.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Gets the type of rate limiting algorithm used by this policy.
    /// </summary>
    string Algorithm { get; }

    /// <summary>
    /// Gets the maximum number of requests allowed per time window.
    /// </summary>
    int MaxRequests { get; }

    /// <summary>
    /// Gets the time window duration.
    /// </summary>
    TimeSpan Window { get; }

    /// <summary>
    /// Gets additional configuration parameters for the policy.
    /// </summary>
    IReadOnlyDictionary<string, object> Parameters { get; }
}

/// <summary>
/// Factory interface for creating rate limiting policies.
/// </summary>
public interface IPolicyFactory
{
    /// <summary>
    /// Creates a rate limiting policy from configuration.
    /// </summary>
    /// <param name="name">Policy name</param>
    /// <param name="algorithm">Algorithm type</param>
    /// <param name="maxRequests">Maximum requests per window</param>
    /// <param name="window">Time window</param>
    /// <param name="parameters">Additional parameters</param>
    /// <returns>Configured policy</returns>
    IPolicy CreatePolicy(string name, string algorithm, int maxRequests, TimeSpan window, IReadOnlyDictionary<string, object>? parameters = null);

    /// <summary>
    /// Creates a rate limiting policy from a configuration object.
    /// </summary>
    /// <param name="config">Policy configuration</param>
    /// <returns>Configured policy</returns>
    IPolicy CreatePolicy(PolicyConfiguration config);
}

/// <summary>
/// Configuration object for rate limiting policies.
/// </summary>
/// <param name="Name">Policy name</param>
/// <param name="Algorithm">Algorithm type (TokenBucket, LeakyBucket, FixedWindow, SlidingWindow)</param>
/// <param name="MaxRequests">Maximum requests per window</param>
/// <param name="Window">Time window duration</param>
/// <param name="Parameters">Additional algorithm-specific parameters</param>
public record PolicyConfiguration(
    string Name,
    string Algorithm,
    int MaxRequests,
    TimeSpan Window,
    IReadOnlyDictionary<string, object>? Parameters = null);
