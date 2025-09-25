using System.Collections.Generic;
using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RateLimiter.Api.Configuration;
using RateLimiter.Api.Policies;
using RateLimiter.Core.Abstractions;

namespace RateLimiter.Api.Identity;

internal sealed class DefaultRateLimitIdentityExtractor : IRateLimitIdentityExtractor
{
    private static readonly char[] HintSeparators = { ',', ';', '|' };

    private readonly IOptionsMonitor<RateLimiterIdentityOptions> _identityOptions;
    private readonly ILogger<DefaultRateLimitIdentityExtractor> _logger;

    public DefaultRateLimitIdentityExtractor(
        IOptionsMonitor<RateLimiterIdentityOptions> identityOptions,
        ILogger<DefaultRateLimitIdentityExtractor> logger)
    {
        _identityOptions = identityOptions;
        _logger = logger;
    }

    public async ValueTask<RateLimitIdentity> ExtractAsync(
        HttpContext context,
        RateLimitPolicy policy,
        RateLimitEndpointMetadata metadata,
        CancellationToken cancellationToken)
    {
        var options = _identityOptions.CurrentValue;

        var apiKey = ResolveDefaultApiKey(context);
        var userId = ResolveUserId(context);
        var ip = context.Connection.RemoteIpAddress?.ToString();
        string? custom = null;

        foreach (var hint in ResolveHints(metadata.IdentityHint))
        {
            if (!options.Selectors.TryGetValue(hint, out var selector))
            {
                continue;
            }

            var value = ResolveConfiguredIdentity(context, selector, ip);
            if (string.IsNullOrEmpty(value))
            {
                continue;
            }

            switch (selector.Component)
            {
                case IdentityComponent.ApiKey:
                    apiKey = value;
                    break;
                case IdentityComponent.UserId:
                    userId = value;
                    break;
                default:
                    custom = value;
                    break;
            }
        }

        if (metadata.CustomKeySelector is not null)
        {
            try
            {
                var configured = await metadata.CustomKeySelector(context, cancellationToken).ConfigureAwait(false);
                if (!string.IsNullOrEmpty(configured))
                {
                    custom = configured;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Custom rate limit key selector failed for policy {Policy}.", policy.PolicyName);
            }
        }

        return new RateLimitIdentity(apiKey, userId, ip, custom);
    }

    private static IEnumerable<string> ResolveHints(string? identityHint)
    {
        if (string.IsNullOrWhiteSpace(identityHint))
        {
            yield break;
        }

        var trimmed = identityHint.Trim();
        if (trimmed.IndexOfAny(HintSeparators) < 0)
        {
            yield return trimmed;
            yield break;
        }

        foreach (var segment in trimmed.Split(HintSeparators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            yield return segment;
        }
    }

    private static string? ResolveConfiguredIdentity(HttpContext context, IdentitySelectionOptions selector, string? ipAddress)
    {
        if (!string.IsNullOrWhiteSpace(selector.Header))
        {
            var value = ReadHeader(context.Request.Headers, selector.Header);
            if (!string.IsNullOrEmpty(value))
            {
                return value;
            }
        }

        if (!string.IsNullOrWhiteSpace(selector.Claim))
        {
            var claim = context.User?.FindFirst(selector.Claim);
            if (!string.IsNullOrEmpty(claim?.Value))
            {
                return claim.Value;
            }
        }

        if (!string.IsNullOrWhiteSpace(selector.RouteValue)
            && context.Request.RouteValues.TryGetValue(selector.RouteValue, out var routeValue)
            && routeValue is not null)
        {
            var value = Convert.ToString(routeValue, CultureInfo.InvariantCulture);
            if (!string.IsNullOrEmpty(value))
            {
                return value;
            }
        }

        if (!string.IsNullOrWhiteSpace(selector.Query)
            && context.Request.Query.TryGetValue(selector.Query, out var queryValues)
            && queryValues.Count > 0)
        {
            return queryValues[0];
        }

        if (selector.UseIpAddressFallback)
        {
            return ipAddress;
        }

        return null;
    }

    private static string? ResolveDefaultApiKey(HttpContext context)
        => ReadHeader(context.Request.Headers, "x-api-key");

    private static string? ReadHeader(IHeaderDictionary headers, string name)
    {
        if (headers.TryGetValue(name, out var values) && values.Count > 0)
        {
            return values[0];
        }

        return null;
    }

    private static string? ResolveUserId(HttpContext context)
    {
        var claim = context.User?.FindFirst(ClaimTypes.NameIdentifier) ?? context.User?.FindFirst("sub");
        if (!string.IsNullOrEmpty(claim?.Value))
        {
            return claim.Value;
        }

        return ReadHeader(context.Request.Headers, "x-user-id");
    }
}
