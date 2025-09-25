using RateLimiter.Core.Abstractions;

namespace RateLimiter.Infrastructure.Persistence;

public interface IRateLimitPolicyRepository
{
    Task<IReadOnlyList<RateLimitPolicy>> GetPoliciesAsync(CancellationToken cancellationToken = default);

    Task UpsertPolicyAsync(RateLimitPolicy policy, CancellationToken cancellationToken = default);
}
