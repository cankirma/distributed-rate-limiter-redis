namespace RateLimiter.Core.Abstractions;

/// <summary>
/// Internal result produced by algorithm evaluation. Infrastructure enriches it into a decision.
/// </summary>
public readonly record struct RateLimitComputationResult(bool IsAllowed, RateLimitCounters Counters, TimeSpan RetryAfter, long EvaluatedAtTicks)
{
    public static readonly RateLimitComputationResult Allowed = new(true, default, TimeSpan.Zero, 0L);
}
