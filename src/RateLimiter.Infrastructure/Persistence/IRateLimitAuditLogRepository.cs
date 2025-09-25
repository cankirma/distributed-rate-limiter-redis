namespace RateLimiter.Infrastructure.Persistence;

public interface IRateLimitAuditLogRepository
{
    Task InsertAsync(RateLimitAuditLogEntry entry, CancellationToken cancellationToken = default);
}
