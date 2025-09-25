namespace RateLimiter.Core.Abstractions;

/// <summary>
/// Supported rate limiting algorithms. Matches the redis scripts and configuration loading.
/// </summary>
public enum RateLimitAlgorithmType : byte
{
    TokenBucket = 0,
    LeakyBucket = 1
}
