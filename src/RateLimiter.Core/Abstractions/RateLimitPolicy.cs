using System.Diagnostics.CodeAnalysis;

namespace RateLimiter.Core.Abstractions;

/// <summary>
/// Represents the persisted configuration for an endpoint rate limiting policy.
/// </summary>
public sealed record RateLimitPolicy
{
    /// <summary>
    /// Unique name of the policy. Typically maps to the endpoint route pattern.
    /// </summary>
    public required string PolicyName { get; init; }

    /// <summary>
    /// The algorithm backing this policy.
    /// </summary>
    public RateLimitAlgorithmType Algorithm { get; init; } = RateLimitAlgorithmType.TokenBucket;

    /// <summary>
    /// The maximum number of requests permitted per window.
    /// </summary>
    public required int PermitLimit { get; init; }

    /// <summary>
    /// The rolling window over which permits are evaluated.
    /// </summary>
    public TimeSpan Window { get; init; } = TimeSpan.FromMinutes(1);

    /// <summary>
    /// Maximum burst capacity beyond the steady rate. Defaults to <see cref="PermitLimit"/> when not specified.
    /// </summary>
    public int BurstLimit { get; init; }

    /// <summary>
    /// Minimum precision for Redis scripts. Controls the resolution of sliding window buckets and retry calculations.
    /// </summary>
    public TimeSpan Precision { get; init; } = TimeSpan.FromMilliseconds(100);

    /// <summary>
    /// Optional cool down duration that must elapse after a block before requests are accepted again.
    /// </summary>
    public TimeSpan? Cooldown { get; init; }

    /// <summary>
    /// Number of tokens consumed per request. Useful for weighting expensive operations.
    /// </summary>
    public uint TokensPerRequest { get; init; } = 1;

    /// <summary>
    /// Whether sliding window metrics are collected for this policy.
    /// </summary>
    public bool SlidingWindowMetricsEnabled { get; init; } = true;

    /// <summary>
    /// Returns the computed burst capacity ensuring it is at least the steady state limit.
    /// </summary>
    public int GetBurstCapacity() => BurstLimit > 0 ? BurstLimit : PermitLimit;

    /// <summary>
    /// Performs light-weight validation of the policy.
    /// </summary>
    public void Validate()
    {
        if (PermitLimit <= 0)
        {
            Throw(nameof(PermitLimit), "Permit limit must be positive.");
        }

        if (Window <= TimeSpan.Zero)
        {
            Throw(nameof(Window), "Window must be a positive duration.");
        }

        if (Precision <= TimeSpan.Zero)
        {
            Throw(nameof(Precision), "Precision must be positive.");
        }

        if (Cooldown is { } cooldown && cooldown <= TimeSpan.Zero)
        {
            Throw(nameof(Cooldown), "Cooldown must be a positive duration when specified.");
        }
    }

    [DoesNotReturn]
    private static void Throw(string property, string message) => throw new InvalidOperationException($"Invalid policy '{property}': {message}");
}
