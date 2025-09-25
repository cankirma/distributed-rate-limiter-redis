using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using RateLimiter.Api.Filters;
using RateLimiter.Api.Policies;

namespace RateLimiter.Api.Extensions;

public static class RateLimitingEndpointConventionExtensions
{
    public static RouteHandlerBuilder RequireRateLimitPolicy(
        this RouteHandlerBuilder builder,
        string policyName,
        RateLimitExecutionMode executionMode = RateLimitExecutionMode.Middleware,
        Func<HttpContext, CancellationToken, ValueTask<uint>>? tokenSelector = null,
        Func<HttpContext, CancellationToken, ValueTask<string?>>? customKeySelector = null,
        string? identityHint = null)
    {
        builder.WithMetadata(new RateLimitEndpointMetadata(policyName, executionMode, tokenSelector, customKeySelector, identityHint));

        if (executionMode == RateLimitExecutionMode.EndpointFilter)
        {
            builder.AddEndpointFilter<RateLimitEndpointFilter>();
        }

        return builder;
    }
}
