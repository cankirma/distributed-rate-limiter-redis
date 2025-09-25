using Microsoft.AspNetCore.Http;
using RateLimiter.Core.Abstractions;
using RateLimiter.Api.Policies;

namespace RateLimiter.Api.Identity;

public interface IRateLimitIdentityExtractor
{
    ValueTask<RateLimitIdentity> ExtractAsync(HttpContext context, RateLimitPolicy policy, RateLimitEndpointMetadata metadata, CancellationToken cancellationToken);
}
