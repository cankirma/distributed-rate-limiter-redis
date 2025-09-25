namespace RateLimiter.Core.Abstractions;

/// <summary>
/// Defines a deterministic rate limiting algorithm operating over an opaque state.
/// </summary>
/// <typeparam name="TState">The state backing the algorithm. Implementations should use structs for minimal allocations.</typeparam>
public interface IRateLimiterAlgorithm<TState> where TState : struct
{
    RateLimitAlgorithmType Algorithm { get; }

    RateLimitComputationResult Evaluate(ref TState state, in RateLimitRequest request, long nowTicks);
}
