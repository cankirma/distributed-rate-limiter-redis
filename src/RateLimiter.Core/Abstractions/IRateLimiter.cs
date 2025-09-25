namespace RateLimiter.Core.Abstractions;

/// <summary>
/// Primary interface consumed by the middleware to check rate limits.
/// Implementations are expected to be thread-safe and allocation conscious.
/// </summary>
public interface IRateLimiter
{
    ValueTask<RateLimitDecision> ShouldAllowAsync(RateLimitRequest request, CancellationToken cancellationToken = default);
}
