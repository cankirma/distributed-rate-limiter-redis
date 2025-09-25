using Dapper;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;
using RateLimiter.Infrastructure.Configuration;

namespace RateLimiter.Infrastructure.Persistence;

internal sealed class RateLimitAuditLogRepository : IRateLimitAuditLogRepository
{
    private readonly RateLimiterInfrastructureOptions _options;
    private readonly ILogger<RateLimitAuditLogRepository> _logger;

    public RateLimitAuditLogRepository(IOptions<RateLimiterInfrastructureOptions> options, ILogger<RateLimitAuditLogRepository> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public async Task InsertAsync(RateLimitAuditLogEntry entry, CancellationToken cancellationToken = default)
    {
        if (!_options.AuditLoggingEnabled)
        {
            _logger.LogDebug("Audit logging disabled; skipping entry for policy {Policy}", entry.PolicyName);
            return;
        }

        await using var connection = new NpgsqlConnection(_options.Postgres.ConnectionString);
        await EnsureSchemaAsync(connection, cancellationToken);

        var sql = $"""
            INSERT INTO {_options.Postgres.Schema}.audit_log (
                policy_name,
                identity_component,
                allowed,
                limit,
                remaining,
                retry_after_milliseconds,
                occurred_at,
                endpoint_path,
                additional_data)
            VALUES (@PolicyName, @IdentityComponent, @Allowed, @Limit, @Remaining, @RetryAfterMilliseconds, @OccurredAt, @EndpointPath, @AdditionalData);
            """;

        await connection.ExecuteAsync(new CommandDefinition(sql, entry, cancellationToken: cancellationToken));
    }

    private async Task EnsureSchemaAsync(NpgsqlConnection connection, CancellationToken cancellationToken)
    {
        if (!_options.Postgres.AutoMigrate)
        {
            return;
        }

        var schemaSql = $"CREATE SCHEMA IF NOT EXISTS {_options.Postgres.Schema};";
        await connection.ExecuteAsync(new CommandDefinition(schemaSql, cancellationToken: cancellationToken));

        var tableSql = $"""
            CREATE TABLE IF NOT EXISTS {_options.Postgres.Schema}.audit_log
            (
                id BIGSERIAL PRIMARY KEY,
                policy_name TEXT NOT NULL,
                identity_component TEXT NOT NULL,
                allowed BOOLEAN NOT NULL,
                limit INTEGER NOT NULL,
                remaining INTEGER NOT NULL,
                retry_after_milliseconds INTEGER NOT NULL,
                occurred_at TIMESTAMPTZ NOT NULL,
                endpoint_path TEXT NULL,
                additional_data JSONB NULL
            );
            """;

        await connection.ExecuteAsync(new CommandDefinition(tableSql, cancellationToken: cancellationToken));
    }
}
