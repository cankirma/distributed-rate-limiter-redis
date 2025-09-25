using System.Threading;
using BenchmarkDotNet.Attributes;
using RateLimiter.Core.Abstractions;
using RateLimiter.Core.Algorithms;
using RateLimiter.Core.Metrics;

namespace RateLimiter.Benchmarks;

[MemoryDiagnoser]
[ThreadingDiagnoser]
public class RateLimiterAlgorithmBenchmarks
{
    private const long TickIncrement = TimeSpan.TicksPerMillisecond;

    private readonly RateLimitPolicy _policy = new()
    {
        PolicyName = "benchmark",
        PermitLimit = 100,
        BurstLimit = 150,
        Window = TimeSpan.FromSeconds(1),
        Precision = TimeSpan.FromMilliseconds(50),
        SlidingWindowMetricsEnabled = true
    };

    private readonly RateLimitIdentity _identity = new("benchmark-key", null, null, null);
    private RateLimitRequest _request;
    private SlidingWindowRateCounter _counter = null!;
    private long _nowTicks;

    [GlobalSetup]
    public void Setup()
    {
        _request = new RateLimitRequest(_policy, _identity);
        _counter = new SlidingWindowRateCounter(TimeSpan.FromSeconds(30), bucketCount: 60);
        _nowTicks = TimeSpan.FromSeconds(5).Ticks;
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _counter.Dispose();
    }

    [Benchmark(Description = "Token bucket - allowed request")]
    public RateLimitComputationResult TokenBucketAllowed()
    {
        var now = _nowTicks + TickIncrement;
        var state = new TokenBucketState(_policy.GetBurstCapacity(), _nowTicks);
        return TokenBucketAlgorithm.Instance.Evaluate(ref state, _request, now);
    }

    [Benchmark(Description = "Token bucket - denied request")]
    public RateLimitComputationResult TokenBucketDenied()
    {
        var now = _nowTicks + TickIncrement;
        var state = new TokenBucketState(0d, _nowTicks);
        return TokenBucketAlgorithm.Instance.Evaluate(ref state, _request, now);
    }

    [Benchmark(Description = "Leaky bucket - allowed request")]
    public RateLimitComputationResult LeakyBucketAllowed()
    {
        var now = _nowTicks + TickIncrement;
        var state = new LeakyBucketState(0d, _nowTicks);
        return LeakyBucketAlgorithm.Instance.Evaluate(ref state, _request, now);
    }

    [Benchmark(Description = "Leaky bucket - denied request")]
    public RateLimitComputationResult LeakyBucketDenied()
    {
        var now = _nowTicks + TickIncrement;
        var state = new LeakyBucketState(_policy.GetBurstCapacity(), _nowTicks);
        return LeakyBucketAlgorithm.Instance.Evaluate(ref state, _request, now);
    }

    [Benchmark(Description = "Sliding window sample update")]
    public SlidingWindowSample SlidingWindowAddSample()
    {
        var now = Interlocked.Add(ref _nowTicks, TickIncrement);
        return _counter.AddSample(now, 1d);
    }
}
