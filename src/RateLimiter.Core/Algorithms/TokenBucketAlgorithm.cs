using System.Runtime.CompilerServices;
using RateLimiter.Core.Abstractions;

namespace RateLimiter.Core.Algorithms;

/// <summary>
/// Token bucket algorithm tuned for distributed execution. State mirrors the data stored in Redis.
/// </summary>
public sealed class TokenBucketAlgorithm : IRateLimiterAlgorithm<TokenBucketState>
{
    public static readonly TokenBucketAlgorithm Instance = new();

    private TokenBucketAlgorithm()
    {
    }

    public RateLimitAlgorithmType Algorithm => RateLimitAlgorithmType.TokenBucket;

    public RateLimitComputationResult Evaluate(ref TokenBucketState state, in RateLimitRequest request, long nowTicks)
    {
        request.EnsureValid();

        var policy = request.Policy;
        var burstCapacity = policy.GetBurstCapacity();
        var evaluatedAt = nowTicks;

        var windowTicks = policy.Window.Ticks;
        if (windowTicks <= 0)
        {
            throw new InvalidOperationException("Policy window must be positive.");
        }

        var refillRatePerTick = (double)policy.PermitLimit / windowTicks;
        if (refillRatePerTick <= double.Epsilon)
        {
            throw new InvalidOperationException("Refill rate resolved to zero; check permit limit and window.");
        }

        var tokens = state.Tokens;
        var lastRefill = state.LastRefillTicks;

        if (lastRefill == 0)
        {
            tokens = burstCapacity;
            lastRefill = nowTicks;
        }

        var elapsedTicks = nowTicks - lastRefill;
        if (elapsedTicks < 0)
        {
            elapsedTicks = 0;
        }

        if (elapsedTicks > 0)
        {
            var accruedTokens = elapsedTicks * refillRatePerTick;
            tokens = Math.Min(burstCapacity, tokens + accruedTokens);
        }

        var requested = Math.Min(request.Tokens, (uint)burstCapacity);
        var requestedAsDouble = requested;

        var allowed = tokens >= requestedAsDouble;
        double used = 0d;
        TimeSpan retryAfter;

        if (allowed)
        {
            tokens -= requestedAsDouble;
            retryAfter = TimeSpan.Zero;
            used = requestedAsDouble;
        }
        else
        {
            var shortage = requestedAsDouble - tokens;
            var ticksUntilSufficient = shortage <= 0d ? policy.Precision.Ticks : (long)Math.Ceiling(shortage / refillRatePerTick);
            if (ticksUntilSufficient < policy.Precision.Ticks)
            {
                ticksUntilSufficient = policy.Precision.Ticks;
            }

            retryAfter = TimeSpan.FromTicks(Math.Min(windowTicks, ticksUntilSufficient));

            if (policy.Cooldown is { } cooldown && cooldown > retryAfter)
            {
                retryAfter = cooldown;
            }
        }

        state = new TokenBucketState(tokens, nowTicks);

        var tokensToFull = Math.Max(0d, burstCapacity - tokens);
        var ticksToFull = tokensToFull <= 0d ? policy.Precision.Ticks : (long)Math.Ceiling(tokensToFull / refillRatePerTick);
        ticksToFull = Math.Clamp(ticksToFull, policy.Precision.Ticks, windowTicks);
        var resetAfter = TimeSpan.FromTicks(ticksToFull);

        var counters = new RateLimitCounters(
            Limit: policy.PermitLimit,
            Remaining: Math.Max(0d, tokens),
            Used: used,
            ResetAfter: resetAfter);

        return new RateLimitComputationResult(allowed, counters, retryAfter, evaluatedAt);
    }
}

/// <summary>
/// State backing the token bucket algorithm. Mirrors the structure stored in Redis for atomic updates.
/// </summary>
public readonly record struct TokenBucketState(double Tokens, long LastRefillTicks)
{
    public static readonly TokenBucketState Empty = new(0d, 0L);
}
