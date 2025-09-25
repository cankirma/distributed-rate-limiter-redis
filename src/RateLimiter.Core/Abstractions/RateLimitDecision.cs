using System.Runtime.CompilerServices;
using RateLimiter.Core.Metrics;

namespace RateLimiter.Core.Abstractions;

/// <summary>
/// Result of running a rate limiting algorithm for a request.
/// </summary>
public readonly record struct RateLimitDecision(bool IsAllowed, RateLimitCounters Counters, SlidingWindowSample SlidingWindow, TimeSpan RetryAfter, long EvaluatedAtTicks)
{
    public static readonly RateLimitDecision Allowed = new(true, new RateLimitCounters(0, 0d, 0d, TimeSpan.Zero), SlidingWindowSample.Empty, TimeSpan.Zero, 0L);

    /// <summary>
    /// Convenience helper for denials with explicit retry.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static RateLimitDecision Denied(RateLimitCounters counters, TimeSpan retryAfter, long evaluatedAtTicks, SlidingWindowSample? sample = null)
        => new(false, counters, sample ?? SlidingWindowSample.Empty, retryAfter, evaluatedAtTicks);
}
