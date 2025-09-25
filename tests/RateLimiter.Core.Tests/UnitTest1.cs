using RateLimiter.Core.Abstractions;
using RateLimiter.Core.Algorithms;
using RateLimiter.Core.Metrics;

namespace RateLimiter.Core.Tests;

public static class TestPolicies
{
    public static RateLimitPolicy Create(
        string name = "test",
        int permitLimit = 5,
        int? burstLimit = null,
        double windowSeconds = 1d,
        double precisionMilliseconds = 100d,
        uint tokensPerRequest = 1,
        TimeSpan? cooldown = null)
    {
        return new RateLimitPolicy
        {
            PolicyName = name,
            PermitLimit = permitLimit,
            BurstLimit = burstLimit ?? permitLimit,
            Window = TimeSpan.FromSeconds(windowSeconds),
            Precision = TimeSpan.FromMilliseconds(precisionMilliseconds),
            TokensPerRequest = tokensPerRequest,
            Cooldown = cooldown,
            SlidingWindowMetricsEnabled = true
        };
    }

    public static RateLimitIdentity Identity(string apiKey = "key")
        => new(apiKey, null, null, null);
}

public class TokenBucketAlgorithmTests
{
    [Fact]
    public void Evaluate_AllowsUpToBurstCapacityAndThenDenies()
    {
        var policy = TestPolicies.Create(permitLimit: 5);
        var request = new RateLimitRequest(policy, TestPolicies.Identity());
    var state = TokenBucketState.Empty;
    var now = TimeSpan.FromMilliseconds(1).Ticks;

        for (var i = 0; i < policy.PermitLimit; i++)
        {
            var decision = TokenBucketAlgorithm.Instance.Evaluate(ref state, request, now);
            Assert.True(decision.IsAllowed);
            Assert.Equal(TimeSpan.Zero, decision.RetryAfter);
        }

        var blocked = TokenBucketAlgorithm.Instance.Evaluate(ref state, request, now);

        Assert.False(blocked.IsAllowed);
        Assert.True(blocked.RetryAfter > TimeSpan.Zero);
    }

    [Fact]
    public void Evaluate_RefillsAfterWindowElapsed()
    {
        var policy = TestPolicies.Create();
        var request = new RateLimitRequest(policy, TestPolicies.Identity());
    var state = TokenBucketState.Empty;
    var now = TimeSpan.FromMilliseconds(1).Ticks;

        for (var i = 0; i < policy.PermitLimit; i++)
        {
            TokenBucketAlgorithm.Instance.Evaluate(ref state, request, now);
        }

    var later = now + TimeSpan.FromSeconds(2).Ticks;
        var decision = TokenBucketAlgorithm.Instance.Evaluate(ref state, request, later);

        Assert.True(decision.IsAllowed);
        Assert.Equal(TimeSpan.Zero, decision.RetryAfter);
    }

    [Fact]
    public void Evaluate_AppliesCooldownWhenConfigured()
    {
        var cooldown = TimeSpan.FromSeconds(3);
        var policy = TestPolicies.Create(cooldown: cooldown);
        var request = new RateLimitRequest(policy, TestPolicies.Identity());
    var state = TokenBucketState.Empty;
    var now = TimeSpan.FromMilliseconds(1).Ticks;

        for (var i = 0; i < policy.PermitLimit; i++)
        {
            TokenBucketAlgorithm.Instance.Evaluate(ref state, request, now);
        }

        var blocked = TokenBucketAlgorithm.Instance.Evaluate(ref state, request, now);

        Assert.False(blocked.IsAllowed);
        Assert.True(blocked.RetryAfter >= cooldown);
    }
}

public class LeakyBucketAlgorithmTests
{
    [Fact]
    public void Evaluate_AllowsRequestsUntilCapacityReached()
    {
        var policy = TestPolicies.Create(permitLimit: 3, burstLimit: 3);
        var request = new RateLimitRequest(policy, TestPolicies.Identity());
    var state = LeakyBucketState.Empty;
    var now = TimeSpan.FromMilliseconds(1).Ticks;

        for (var i = 0; i < policy.PermitLimit; i++)
        {
            var decision = LeakyBucketAlgorithm.Instance.Evaluate(ref state, request, now);
            Assert.True(decision.IsAllowed);
        }

        var blocked = LeakyBucketAlgorithm.Instance.Evaluate(ref state, request, now);
        Assert.False(blocked.IsAllowed);
    }

    [Fact]
    public void Evaluate_LeaksWaterOverTime()
    {
        var policy = TestPolicies.Create(permitLimit: 2, burstLimit: 2, windowSeconds: 2);
        var request = new RateLimitRequest(policy, TestPolicies.Identity());
    var state = LeakyBucketState.Empty;
    var now = TimeSpan.FromMilliseconds(1).Ticks;

        for (var i = 0; i < policy.PermitLimit; i++)
        {
            LeakyBucketAlgorithm.Instance.Evaluate(ref state, request, now);
        }

        var next = LeakyBucketAlgorithm.Instance.Evaluate(ref state, request, now);
        Assert.False(next.IsAllowed);

    var later = now + TimeSpan.FromSeconds(3).Ticks;
        var allowedAfterLeak = LeakyBucketAlgorithm.Instance.Evaluate(ref state, request, later);
        Assert.True(allowedAfterLeak.IsAllowed);
    }
}

public class RateLimitPolicyTests
{
    [Fact]
    public void Validate_ThrowsWhenPermitLimitIsNonPositive()
    {
        var policy = new RateLimitPolicy
        {
            PolicyName = "invalid",
            PermitLimit = 0,
            Window = TimeSpan.FromSeconds(1),
            Precision = TimeSpan.FromMilliseconds(100)
        };

        Assert.Throws<InvalidOperationException>(policy.Validate);
    }

    [Fact]
    public void GetBurstCapacity_UsesPermitLimitWhenBurstNotSpecified()
    {
        var policy = TestPolicies.Create(permitLimit: 10, burstLimit: 0);

        Assert.Equal(policy.PermitLimit, policy.GetBurstCapacity());
    }

    [Fact]
    public void Validate_PassesForValidPolicy()
    {
        var policy = TestPolicies.Create();

        policy.Validate();
    }
}

public class SlidingWindowRateCounterTests
{
    [Fact]
    public void AddSample_ComputesHitsAndRate()
    {
        var counter = new SlidingWindowRateCounter(TimeSpan.FromSeconds(10), bucketCount: 5);
        var now = TimeSpan.FromSeconds(100).Ticks;

        counter.AddSample(now, 5);
        var sample = counter.Snapshot(now);

    Assert.Equal(5d, sample.Hits, 5);
    Assert.Equal(0.5d, sample.RatePerSecond, 5);
    }

    [Fact]
    public void Snapshot_DiscardsSamplesOutsideWindow()
    {
        var counter = new SlidingWindowRateCounter(TimeSpan.FromSeconds(5), bucketCount: 5);
        var start = TimeSpan.FromSeconds(0).Ticks;

        counter.AddSample(start, 3);

        var later = TimeSpan.FromSeconds(10).Ticks;
        var sample = counter.Snapshot(later);

    Assert.Equal(0d, sample.Hits, 5);
    Assert.Equal(0d, sample.RatePerSecond, 5);
    }
}
