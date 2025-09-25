using System.Runtime.CompilerServices;

namespace RateLimiter.Core.Abstractions;

/// <summary>
/// Represents an incoming rate limited action.
/// </summary>
public readonly record struct RateLimitRequest(RateLimitPolicy Policy, RateLimitIdentity Identity, uint Tokens = 1)
{
    /// <summary>
    /// Pre-validates the request. Meant to be called prior to expensive work.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void EnsureValid()
    {
        Policy.Validate();
        if (Tokens == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(Tokens), "Requested tokens must be at least 1.");
        }
    }
}
