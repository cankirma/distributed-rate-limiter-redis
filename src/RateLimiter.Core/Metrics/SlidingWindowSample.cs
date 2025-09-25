namespace RateLimiter.Core.Metrics;

/// <summary>
/// Snapshot of activity over a sliding window used for Prometheus metrics.
/// </summary>
public readonly record struct SlidingWindowSample(TimeSpan Window, double Hits, double RatePerSecond)
{
    public static readonly SlidingWindowSample Empty = new(TimeSpan.Zero, 0d, 0d);

    public bool HasData => Hits > 0 && Window > TimeSpan.Zero;
}
