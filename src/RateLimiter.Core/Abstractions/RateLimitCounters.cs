using System.Runtime.CompilerServices;

namespace RateLimiter.Core.Abstractions;

/// <summary>
/// Captures the counter information used to populate rate limiting headers.
/// </summary>
public readonly record struct RateLimitCounters(int Limit, double Remaining, double Used, TimeSpan ResetAfter)
{
    /// <summary>
    /// The Retry-After duration derived from ResetAfter when the request was denied.
    /// </summary>
    public TimeSpan RetryAfter => ResetAfter < TimeSpan.Zero ? TimeSpan.Zero : ResetAfter;

    /// <summary>
    /// Formats the remaining quota as an integer for header serialization with minimal rounding.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int RemainingAsInt() => (int)Math.Max(0, Math.Floor(Remaining));

    /// <summary>
    /// Formats the used quota as an integer for header serialization.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int UsedAsInt() => (int)Math.Max(0, Math.Ceiling(Used));
}
