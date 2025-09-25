using Microsoft.AspNetCore.Http;

namespace RateLimiter.Api.Policies;

public enum RateLimitExecutionMode
{
    Middleware,
    EndpointFilter
}

public sealed class RateLimitEndpointMetadata
{
    public RateLimitEndpointMetadata(
        string policyName,
        RateLimitExecutionMode executionMode = RateLimitExecutionMode.Middleware,
        Func<HttpContext, CancellationToken, ValueTask<uint>>? tokenSelector = null,
        Func<HttpContext, CancellationToken, ValueTask<string?>>? customKeySelector = null,
        string? identityHint = null)
    {
        PolicyName = policyName ?? throw new ArgumentNullException(nameof(policyName));
        ExecutionMode = executionMode;
        TokenSelector = tokenSelector;
        CustomKeySelector = customKeySelector;
        IdentityHint = identityHint;
    }

    public string PolicyName { get; }

    public RateLimitExecutionMode ExecutionMode { get; }

    public Func<HttpContext, CancellationToken, ValueTask<uint>>? TokenSelector { get; }

    public Func<HttpContext, CancellationToken, ValueTask<string?>>? CustomKeySelector { get; }

    public string? IdentityHint { get; }
}
