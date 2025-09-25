using System.Data;
using Dapper;
using Microsoft.Extensions.Options;
using Npgsql;
using RateLimiter.Core.Abstractions;
using RateLimiter.Infrastructure.Configuration;

namespace RateLimiter.Infrastructure.Persistence;

internal sealed class RateLimitPolicyRepository : IRateLimitPolicyRepository
{
    private readonly RateLimiterInfrastructureOptions _options;

    public RateLimitPolicyRepository(IOptions<RateLimiterInfrastructureOptions> options)
    {
        _options = options.Value;
    }

    public async Task<IReadOnlyList<RateLimitPolicy>> GetPoliciesAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await EnsureSchemaAsync(connection, cancellationToken);

    var sql = $"""
    SELECT policy_name, algorithm, permit_limit, window_seconds, burst_limit, precision_milliseconds, cooldown_seconds, tokens_per_request, sliding_window_enabled
    FROM {_options.Postgres.Schema}.policies
    """;

        var rows = await connection.QueryAsync(sql);
        var list = new List<RateLimitPolicy>();
        foreach (var row in rows)
        {
            var policy = new RateLimitPolicy
            {
                PolicyName = row.policy_name,
                Algorithm = (RateLimitAlgorithmType)row.algorithm,
                PermitLimit = row.permit_limit,
                Window = TimeSpan.FromSeconds((double)row.window_seconds),
                BurstLimit = row.burst_limit,
                Precision = TimeSpan.FromMilliseconds((double)row.precision_milliseconds),
                Cooldown = row.cooldown_seconds is null ? null : TimeSpan.FromSeconds((double)row.cooldown_seconds),
                TokensPerRequest = (uint)row.tokens_per_request,
                SlidingWindowMetricsEnabled = row.sliding_window_enabled
            };

            list.Add(policy);
        }

        return list;
    }

    public async Task UpsertPolicyAsync(RateLimitPolicy policy, CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await EnsureSchemaAsync(connection, cancellationToken);

        const string sql = """
            INSERT INTO {schema}.policies (policy_name, algorithm, permit_limit, window_seconds, burst_limit, precision_milliseconds, cooldown_seconds, tokens_per_request, sliding_window_enabled)
            VALUES (@PolicyName, @Algorithm, @PermitLimit, @WindowSeconds, @BurstLimit, @PrecisionMilliseconds, @CooldownSeconds, @TokensPerRequest, @SlidingWindowEnabled)
            ON CONFLICT (policy_name)
            DO UPDATE SET algorithm = EXCLUDED.algorithm,
                          permit_limit = EXCLUDED.permit_limit,
                          window_seconds = EXCLUDED.window_seconds,
                          burst_limit = EXCLUDED.burst_limit,
                          precision_milliseconds = EXCLUDED.precision_milliseconds,
                          cooldown_seconds = EXCLUDED.cooldown_seconds,
                          tokens_per_request = EXCLUDED.tokens_per_request,
                          sliding_window_enabled = EXCLUDED.sliding_window_enabled;
            """;

        var parameters = new
        {
            policy.PolicyName,
            Algorithm = (int)policy.Algorithm,
            policy.PermitLimit,
            WindowSeconds = policy.Window.TotalSeconds,
            policy.BurstLimit,
            PrecisionMilliseconds = policy.Precision.TotalMilliseconds,
            CooldownSeconds = policy.Cooldown?.TotalSeconds,
            TokensPerRequest = (int)policy.TokensPerRequest,
            policy.SlidingWindowMetricsEnabled
        };

        var formattedSql = sql.Replace("{schema}", _options.Postgres.Schema);
        await connection.ExecuteAsync(new CommandDefinition(formattedSql, parameters, cancellationToken: cancellationToken));
    }

    private NpgsqlConnection CreateConnection() => new(_options.Postgres.ConnectionString);

    private async Task EnsureSchemaAsync(IDbConnection connection, CancellationToken cancellationToken)
    {
        if (!_options.Postgres.AutoMigrate)
        {
            return;
        }

    var schemaSql = $"CREATE SCHEMA IF NOT EXISTS {_options.Postgres.Schema};";
        await connection.ExecuteAsync(new CommandDefinition(schemaSql, cancellationToken: cancellationToken));

        var tableSql = $"""
        CREATE TABLE IF NOT EXISTS {_options.Postgres.Schema}.policies
        (
            policy_name TEXT PRIMARY KEY,
            algorithm SMALLINT NOT NULL,
            permit_limit INTEGER NOT NULL,
            window_seconds DOUBLE PRECISION NOT NULL,
            burst_limit INTEGER NOT NULL,
            precision_milliseconds DOUBLE PRECISION NOT NULL,
            cooldown_seconds DOUBLE PRECISION NULL,
            tokens_per_request INTEGER NOT NULL,
            sliding_window_enabled BOOLEAN NOT NULL
        );
        """;

        await connection.ExecuteAsync(new CommandDefinition(tableSql, cancellationToken: cancellationToken));
    }
}
