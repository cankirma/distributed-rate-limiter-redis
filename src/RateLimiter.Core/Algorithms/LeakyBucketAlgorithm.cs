using System.Runtime.CompilerServices;
using RateLimiter.Core.Abstractions;

namespace RateLimiter.Core.Algorithms;

/// <summary>
/// Classic leaky bucket algorithm with burst allowance. Designed for distributed state stored in Redis.
/// </summary>
public sealed class LeakyBucketAlgorithm : IRateLimiterAlgorithm<LeakyBucketState>
{
    public static readonly LeakyBucketAlgorithm Instance = new();

    private LeakyBucketAlgorithm()
    {
    }

    public RateLimitAlgorithmType Algorithm => RateLimitAlgorithmType.LeakyBucket;

    public RateLimitComputationResult Evaluate(ref LeakyBucketState state, in RateLimitRequest request, long nowTicks)
    {
        request.EnsureValid();

        var policy = request.Policy;
        var burstCapacity = policy.GetBurstCapacity();
        var windowTicks = policy.Window.Ticks;
        if (windowTicks <= 0)
        {
            throw new InvalidOperationException("Policy window must be positive.");
        }

        var leakRatePerTick = (double)policy.PermitLimit / windowTicks;
        if (leakRatePerTick <= double.Epsilon)
        {
            throw new InvalidOperationException("Leak rate resolved to zero; check permit limit and window.");
        }

        var waterLevel = state.WaterLevel;
        var lastDrip = state.LastDripTicks;

        if (lastDrip == 0)
        {
            lastDrip = nowTicks;
            waterLevel = 0d;
        }

        var elapsedTicks = nowTicks - lastDrip;
        if (elapsedTicks < 0)
        {
            elapsedTicks = 0;
        }

        if (elapsedTicks > 0)
        {
            var leaked = elapsedTicks * leakRatePerTick;
            waterLevel = Math.Max(0d, waterLevel - leaked);
        }

        var requested = Math.Min(request.Tokens, (uint)burstCapacity);
        var requestedAsDouble = requested;

        var newLevel = waterLevel + requestedAsDouble;
        var allowed = newLevel <= burstCapacity;
        double used;
        TimeSpan retryAfter;

        if (allowed)
        {
            waterLevel = newLevel;
            used = requestedAsDouble;
            retryAfter = TimeSpan.Zero;
        }
        else
        {
            used = 0d;
            var overflow = newLevel - burstCapacity;
            var ticksUntilCapacity = overflow <= 0d ? policy.Precision.Ticks : (long)Math.Ceiling(overflow / leakRatePerTick);
            if (ticksUntilCapacity < policy.Precision.Ticks)
            {
                ticksUntilCapacity = policy.Precision.Ticks;
            }

            retryAfter = TimeSpan.FromTicks(Math.Min(windowTicks, ticksUntilCapacity));

            if (policy.Cooldown is { } cooldown && cooldown > retryAfter)
            {
                retryAfter = cooldown;
            }
        }

        state = new LeakyBucketState(waterLevel, nowTicks);

        var remaining = Math.Max(0d, burstCapacity - waterLevel);
        var usedTotal = burstCapacity - remaining;
        var ticksToDrain = waterLevel <= 0d ? policy.Precision.Ticks : (long)Math.Ceiling(waterLevel / leakRatePerTick);
        ticksToDrain = Math.Clamp(ticksToDrain, policy.Precision.Ticks, windowTicks);

        var counters = new RateLimitCounters(
            Limit: policy.PermitLimit,
            Remaining: remaining,
            Used: usedTotal,
            ResetAfter: TimeSpan.FromTicks(ticksToDrain));

        return new RateLimitComputationResult(allowed, counters, retryAfter, nowTicks);
    }
}

/// <summary>
/// State backing the leaky bucket algorithm stored alongside redis keys.
/// </summary>
public readonly record struct LeakyBucketState(double WaterLevel, long LastDripTicks)
{
    public static readonly LeakyBucketState Empty = new(0d, 0L);
}
