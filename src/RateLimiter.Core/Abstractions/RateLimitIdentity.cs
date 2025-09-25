using System.Runtime.CompilerServices;

namespace RateLimiter.Core.Abstractions;

/// <summary>
/// Encapsulates the caller identity resolved by the middleware pipeline.
/// </summary>
public readonly record struct RateLimitIdentity(string? ApiKey, string? UserId, string? IpAddress, string? CustomDiscriminator)
{
    /// <summary>
    /// Determines whether all identity components are missing.
    /// </summary>
    public bool IsAnonymous => string.IsNullOrEmpty(ApiKey) && string.IsNullOrEmpty(UserId) && string.IsNullOrEmpty(CustomDiscriminator);

    /// <summary>
    /// Builds a deterministic identity key with minimal allocations.
    /// </summary>
    public string ComposeStorageKey(string policyName)
    {
        var (prefix, component) = ResolveKeyParts();
        component ??= "anon";

        return string.Create(policyName.Length + 1 + prefix.Length + component.Length, (policyName, prefix, component), static (span, state) =>
        {
            var (policy, prefix, component) = state;
            policy.AsSpan().CopyTo(span);
            var index = policy.Length;
            span[index++] = ':';
            if (prefix.Length > 0)
            {
                prefix.AsSpan().CopyTo(span[index..]);
                index += prefix.Length;
            }

            component.AsSpan().CopyTo(span[index..]);
        });
    }

    /// <summary>
    /// Returns the most specific identity component available.
    /// </summary>
    public string? MostSpecificComponent => CustomDiscriminator ?? ApiKey ?? UserId ?? IpAddress;

    /// <summary>
    /// Normalises the identity into a canonical form used for metrics tagging.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public string GetMetricLabel()
    {
        if (!string.IsNullOrEmpty(CustomDiscriminator))
        {
            return "custom";
        }

        if (!string.IsNullOrEmpty(ApiKey))
        {
            return "api-key";
        }

        if (!string.IsNullOrEmpty(UserId))
        {
            return "user";
        }

        return string.IsNullOrEmpty(IpAddress) ? "anonymous" : "ip";
    }

    private (string Prefix, string? Component) ResolveKeyParts()
    {
        if (!string.IsNullOrEmpty(CustomDiscriminator))
        {
            return (string.Empty, CustomDiscriminator);
        }

        if (!string.IsNullOrEmpty(ApiKey))
        {
            return ("api:", ApiKey);
        }

        if (!string.IsNullOrEmpty(UserId))
        {
            return ("user:", UserId);
        }

        if (!string.IsNullOrEmpty(IpAddress))
        {
            return ("ip:", IpAddress);
        }

        return (string.Empty, null);
    }
}
