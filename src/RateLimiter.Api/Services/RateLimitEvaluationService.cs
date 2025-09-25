using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using RateLimiter.Api.Identity;
using RateLimiter.Api.Policies;
using RateLimiter.Core.Abstractions;
using RateLimiter.Infrastructure.Services;

namespace RateLimiter.Api.Services;

internal interface IRateLimitEvaluationService
{
    ValueTask<RateLimitEvaluationResult?> EvaluateAsync(HttpContext context, RateLimitEndpointMetadata metadata, CancellationToken cancellationToken);
}

internal sealed class RateLimitEvaluationService : IRateLimitEvaluationService
{
    private readonly IRateLimiter _rateLimiter;
    private readonly IRateLimitPolicyCache _policyCache;
    private readonly IRateLimitIdentityExtractor _identityExtractor;
    private readonly ILogger<RateLimitEvaluationService> _logger;

    public RateLimitEvaluationService(
        IRateLimiter rateLimiter,
        IRateLimitPolicyCache policyCache,
        IRateLimitIdentityExtractor identityExtractor,
        ILogger<RateLimitEvaluationService> logger)
    {
        _rateLimiter = rateLimiter;
        _policyCache = policyCache;
        _identityExtractor = identityExtractor;
        _logger = logger;
    }

    public async ValueTask<RateLimitEvaluationResult?> EvaluateAsync(HttpContext context, RateLimitEndpointMetadata metadata, CancellationToken cancellationToken)
    {
        var policy = _policyCache.GetPolicy(metadata.PolicyName);
        if (policy is null)
        {
            _logger.LogWarning("Rate limit policy {Policy} not found.", metadata.PolicyName);
            return null;
        }

        var tokens = await ResolveTokensAsync(context, metadata, cancellationToken).ConfigureAwait(false);
        var identity = await _identityExtractor.ExtractAsync(context, policy, metadata, cancellationToken).ConfigureAwait(false);

        var request = new RateLimitRequest(policy, identity, tokens);
        var decision = await _rateLimiter.ShouldAllowAsync(request, cancellationToken).ConfigureAwait(false);

        return new RateLimitEvaluationResult(policy, identity, request, decision);
    }

    private static async ValueTask<uint> ResolveTokensAsync(HttpContext context, RateLimitEndpointMetadata metadata, CancellationToken cancellationToken)
    {
        if (metadata.TokenSelector is null)
        {
            return 1;
        }

        var value = await metadata.TokenSelector(context, cancellationToken).ConfigureAwait(false);
        return value == 0 ? 1u : value;
    }
}

internal sealed record RateLimitEvaluationResult(
    RateLimitPolicy Policy,
    RateLimitIdentity Identity,
    RateLimitRequest Request,
    RateLimitDecision Decision);
