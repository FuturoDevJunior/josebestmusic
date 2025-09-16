using System.Text.RegularExpressions;

namespace NetThrottler.Core.Security;

/// <summary>
/// Provides security validation for throttling operations.
/// </summary>
public static class SecurityValidator
{
    private static readonly Regex KeyPattern = new(@"^[a-zA-Z0-9._-]+$", RegexOptions.Compiled);
    private static readonly Regex PolicyNamePattern = new(@"^[a-zA-Z0-9._-]+$", RegexOptions.Compiled);
    private static readonly int MaxKeyLength = 256;
    private static readonly int MaxPolicyNameLength = 64;
    private static readonly int MaxValueLength = 1024;

    /// <summary>
    /// Validates a throttling key for security.
    /// </summary>
    /// <param name="key">The key to validate.</param>
    /// <returns>True if the key is valid, false otherwise.</returns>
    public static bool IsValidKey(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
            return false;

        if (key.Length > MaxKeyLength)
            return false;

        // Check for potential injection patterns
        if (ContainsInjectionPatterns(key))
            return false;

        return KeyPattern.IsMatch(key);
    }

    /// <summary>
    /// Validates a policy name for security.
    /// </summary>
    /// <param name="policyName">The policy name to validate.</param>
    /// <returns>True if the policy name is valid, false otherwise.</returns>
    public static bool IsValidPolicyName(string policyName)
    {
        if (string.IsNullOrWhiteSpace(policyName))
            return false;

        if (policyName.Length > MaxPolicyNameLength)
            return false;

        // Check for potential injection patterns
        if (ContainsInjectionPatterns(policyName))
            return false;

        return PolicyNamePattern.IsMatch(policyName);
    }

    /// <summary>
    /// Validates a storage value for security.
    /// </summary>
    /// <param name="value">The value to validate.</param>
    /// <returns>True if the value is valid, false otherwise.</returns>
    public static bool IsValidValue(string value)
    {
        if (value == null)
            return true; // null values are allowed

        if (value.Length > MaxValueLength)
            return false;

        // Check for potential injection patterns
        if (ContainsInjectionPatterns(value))
            return false;

        return true;
    }

    /// <summary>
    /// Sanitizes a key by removing potentially dangerous characters.
    /// </summary>
    /// <param name="key">The key to sanitize.</param>
    /// <returns>The sanitized key.</returns>
    public static string SanitizeKey(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
            return string.Empty;

        // Remove potentially dangerous characters
        var sanitized = Regex.Replace(key, @"[^a-zA-Z0-9._-]", "_");

        // Limit length
        if (sanitized.Length > MaxKeyLength)
            sanitized = sanitized.Substring(0, MaxKeyLength);

        return sanitized;
    }

    /// <summary>
    /// Sanitizes a policy name by removing potentially dangerous characters.
    /// </summary>
    /// <param name="policyName">The policy name to sanitize.</param>
    /// <returns>The sanitized policy name.</returns>
    public static string SanitizePolicyName(string policyName)
    {
        if (string.IsNullOrWhiteSpace(policyName))
            return string.Empty;

        // Remove potentially dangerous characters
        var sanitized = Regex.Replace(policyName, @"[^a-zA-Z0-9._-]", "_");

        // Limit length
        if (sanitized.Length > MaxPolicyNameLength)
            sanitized = sanitized.Substring(0, MaxPolicyNameLength);

        return sanitized;
    }

    /// <summary>
    /// Validates throttling parameters for security.
    /// </summary>
    /// <param name="capacity">The capacity parameter.</param>
    /// <param name="refillRate">The refill rate parameter.</param>
    /// <param name="windowSize">The window size parameter.</param>
    /// <returns>True if the parameters are valid, false otherwise.</returns>
    public static bool IsValidThrottlingParameters(int capacity, double refillRate, TimeSpan windowSize)
    {
        // Validate capacity
        if (capacity <= 0 || capacity > 1000000)
            return false;

        // Validate refill rate
        if (refillRate <= 0 || refillRate > 1000000)
            return false;

        // Validate window size
        if (windowSize <= TimeSpan.Zero || windowSize > TimeSpan.FromDays(1))
            return false;

        return true;
    }

    /// <summary>
    /// Validates a TTL (Time To Live) parameter for security.
    /// </summary>
    /// <param name="ttl">The TTL to validate.</param>
    /// <returns>True if the TTL is valid, false otherwise.</returns>
    public static bool IsValidTtl(TimeSpan ttl)
    {
        // TTL should be positive and not too long (max 24 hours)
        return ttl > TimeSpan.Zero && ttl <= TimeSpan.FromDays(1);
    }

    private static bool ContainsInjectionPatterns(string input)
    {
        // Check for common injection patterns
        var injectionPatterns = new[]
        {
            @"<script",
            @"javascript:",
            @"vbscript:",
            @"onload\s*=",
            @"onerror\s*=",
            @"eval\s*\(",
            @"expression\s*\(",
            @"url\s*\(",
            @"@import",
            @"\x00", // null byte
            @"\x1a", // substitute character
            @"\x1b", // escape character
        };

        foreach (var pattern in injectionPatterns)
        {
            if (Regex.IsMatch(input, pattern, RegexOptions.IgnoreCase))
                return true;
        }

        return false;
    }
}

/// <summary>
/// Security configuration for throttling operations.
/// </summary>
public class SecurityConfiguration
{
    /// <summary>
    /// Gets or sets the maximum key length.
    /// </summary>
    public int MaxKeyLength { get; set; } = 256;

    /// <summary>
    /// Gets or sets the maximum policy name length.
    /// </summary>
    public int MaxPolicyNameLength { get; set; } = 64;

    /// <summary>
    /// Gets or sets the maximum value length.
    /// </summary>
    public int MaxValueLength { get; set; } = 1024;

    /// <summary>
    /// Gets or sets the maximum capacity for throttling policies.
    /// </summary>
    public int MaxCapacity { get; set; } = 1000000;

    /// <summary>
    /// Gets or sets the maximum refill rate for throttling policies.
    /// </summary>
    public double MaxRefillRate { get; set; } = 1000000;

    /// <summary>
    /// Gets or sets the maximum window size for throttling policies.
    /// </summary>
    public TimeSpan MaxWindowSize { get; set; } = TimeSpan.FromDays(1);

    /// <summary>
    /// Gets or sets the maximum TTL for storage operations.
    /// </summary>
    public TimeSpan MaxTtl { get; set; } = TimeSpan.FromDays(1);

    /// <summary>
    /// Gets or sets whether to enable strict validation.
    /// </summary>
    public bool EnableStrictValidation { get; set; } = true;

    /// <summary>
    /// Gets or sets whether to log security violations.
    /// </summary>
    public bool LogSecurityViolations { get; set; } = true;
}
