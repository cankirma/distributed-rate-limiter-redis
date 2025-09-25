using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace RateLimiter.Api.Configuration;

public sealed class RateLimiterIdentityOptions
{
    public const string SectionName = "RateLimiter:Identity";

    public Dictionary<string, IdentitySelectionOptions> Selectors { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    public void Validate()
    {
        foreach (var (name, selector) in Selectors)
        {
            selector.Validate(name);
        }
    }
}

public sealed class IdentitySelectionOptions
{
    public IdentityComponent Component { get; init; } = IdentityComponent.Custom;

    public string? Header { get; init; }

    public string? Claim { get; init; }

    public string? RouteValue { get; init; }

    public string? Query { get; init; }

    public bool UseIpAddressFallback { get; init; }

    internal void Validate(string name)
    {
        if (string.IsNullOrWhiteSpace(Header)
            && string.IsNullOrWhiteSpace(Claim)
            && string.IsNullOrWhiteSpace(RouteValue)
            && string.IsNullOrWhiteSpace(Query)
            && !UseIpAddressFallback)
        {
            throw new ValidationException($"Identity selector '{name}' must specify at least one value source.");
        }
    }
}

public enum IdentityComponent
{
    ApiKey,
    UserId,
    Custom
}
