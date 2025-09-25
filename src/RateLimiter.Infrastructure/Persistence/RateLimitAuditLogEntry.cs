namespace RateLimiter.Infrastructure.Persistence;

public sealed record RateLimitAuditLogEntry
{
    public required string PolicyName { get; init; }

    public required string IdentityComponent { get; init; }

    public required bool Allowed { get; init; }

    public required int Limit { get; init; }

    public required int Remaining { get; init; }

    public required int RetryAfterMilliseconds { get; init; }

    public DateTimeOffset OccurredAt { get; init; } = DateTimeOffset.UtcNow;

    public string? EndpointPath { get; init; }

    public string? AdditionalData { get; init; }
}
