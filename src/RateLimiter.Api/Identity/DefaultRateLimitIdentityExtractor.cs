using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using RateLimiter.Api.Policies;
using RateLimiter.Core.Abstractions;

namespace RateLimiter.Api.Identity;

internal sealed class DefaultRateLimitIdentityExtractor : IRateLimitIdentityExtractor
{
    private readonly ILogger<DefaultRateLimitIdentityExtractor> _logger;

    public DefaultRateLimitIdentityExtractor(ILogger<DefaultRateLimitIdentityExtractor> logger)
    {
        _logger = logger;
    }

    public async ValueTask<RateLimitIdentity> ExtractAsync(HttpContext context, RateLimitPolicy policy, RateLimitEndpointMetadata metadata, CancellationToken cancellationToken)
    {
        string? custom = null;
        if (metadata.CustomKeySelector is not null)
        {
            try
            {
                custom = await metadata.CustomKeySelector(context, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Custom rate limit key selector failed for policy {Policy}.", policy.PolicyName);
            }
        }

        var apiKey = ReadHeader(context.Request.Headers, "x-api-key");
        var userId = ResolveUserId(context);
        var ip = context.Connection.RemoteIpAddress?.ToString();

        return new RateLimitIdentity(apiKey, userId, ip, custom);
    }

    private static string? ReadHeader(IHeaderDictionary headers, string name)
    {
        if (headers.TryGetValue(name, out var values) && values.Count > 0)
        {
            return values![0];
        }

        return null;
    }

    private static string? ResolveUserId(HttpContext context)
    {
        var claim = context.User?.FindFirst(ClaimTypes.NameIdentifier) ?? context.User?.FindFirst("sub");
        if (!string.IsNullOrEmpty(claim?.Value))
        {
            return claim!.Value;
        }

        return ReadHeader(context.Request.Headers, "x-user-id");
    }
}
