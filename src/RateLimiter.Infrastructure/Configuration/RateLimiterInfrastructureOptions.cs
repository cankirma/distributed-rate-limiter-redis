using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using RateLimiter.Core.Abstractions;

namespace RateLimiter.Infrastructure.Configuration;

/// <summary>
/// Root options model for infrastructure services. Bound via <c>IOptionsMonitor&lt;RateLimiterInfrastructureOptions&gt;</c>.
/// </summary>
public sealed class RateLimiterInfrastructureOptions
{
    public const string SectionName = "RateLimiter";

    public RedisOptions Redis { get; init; } = new();

    public PostgresOptions Postgres { get; init; } = new();

    /// <summary>
    /// Optional statically configured policies that are merged with persisted definitions.
    /// </summary>
    public IReadOnlyList<RateLimitPolicyConfiguration> Policies { get; init; } = Array.Empty<RateLimitPolicyConfiguration>();

    /// <summary>
    /// Determines how frequently policies are refreshed from Postgres.
    /// </summary>
    [Range(typeof(TimeSpan), "00:00:05", "1.00:00:00")]
    public TimeSpan PolicyReloadInterval { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// When true, audit log entries are persisted for rejections.
    /// </summary>
    public bool AuditLoggingEnabled { get; init; } = true;

    /// <summary>
    /// Configuration for local sliding window metrics.
    /// </summary>
    public SlidingWindowOptions SlidingWindow { get; init; } = new();

    /// <summary>
    /// Whether to eagerly fetch policies during startup.
    /// </summary>
    public bool WarmPoliciesOnStartup { get; init; } = true;

    public void Validate()
    {
        Redis.Validate();
        Postgres.Validate();
        SlidingWindow.Validate();

        foreach (var policy in Policies)
        {
            policy.ToPolicy();
        }

        if (PolicyReloadInterval <= TimeSpan.Zero)
        {
            throw new ValidationException("Policy reload interval must be positive.");
        }
    }

    public sealed class RateLimitPolicyConfiguration
    {
        [Required]
        public string PolicyName { get; init; } = string.Empty;

        public RateLimitAlgorithmType Algorithm { get; init; } = RateLimitAlgorithmType.TokenBucket;

        [Range(1, int.MaxValue)]
        public int PermitLimit { get; init; } = 100;

        [Range(typeof(TimeSpan), "00:00:01", "1.00:00:00")]
        public TimeSpan Window { get; init; } = TimeSpan.FromMinutes(1);

        [Range(0, int.MaxValue)]
        public int BurstLimit { get; init; }

        [Range(typeof(TimeSpan), "00:00:01", "00:10:00")]
        public TimeSpan Precision { get; init; } = TimeSpan.FromMilliseconds(100);

        public TimeSpan? Cooldown { get; init; }

        [Range(1, int.MaxValue)]
        public int TokensPerRequest { get; init; } = 1;

        public bool SlidingWindowMetricsEnabled { get; init; } = true;

        public RateLimitPolicy ToPolicy()
        {
            if (string.IsNullOrWhiteSpace(PolicyName))
            {
                throw new ValidationException("Policy name is required.");
            }

            var policy = new RateLimitPolicy
            {
                PolicyName = PolicyName,
                Algorithm = Algorithm,
                PermitLimit = PermitLimit,
                Window = Window,
                BurstLimit = BurstLimit,
                Precision = Precision,
                Cooldown = Cooldown,
                TokensPerRequest = (uint)TokensPerRequest,
                SlidingWindowMetricsEnabled = SlidingWindowMetricsEnabled
            };

            policy.Validate();
            return policy;
        }
    }

    public sealed class RedisOptions
    {
        /// <summary>
        /// StackExchange.Redis configuration string. Supports sentinel/cluster syntax.
        /// </summary>
        [Required]
        public string ConnectionString { get; init; } = "localhost:6379";

        /// <summary>
        /// Prefix for rate limiter keys. Allows segregating multi-tenant environments.
        /// </summary>
        [Required]
        public string KeyPrefix { get; init; } = "rl";

        /// <summary>
        /// Expiry applied to rate limiter keys when tokens remain.
        /// </summary>
        [Range(typeof(TimeSpan), "00:00:10", "1.00:00:00")]
        public TimeSpan KeyTtl { get; init; } = TimeSpan.FromMinutes(10);

        /// <summary>
        /// Optional name for the physical Redis database (db index).
        /// </summary>
        public int? Database { get; init; }

        /// <summary>
        /// Maximum pool size for multiplexers used by Lua script execution.
        /// </summary>
        [Range(1, 1024)]
        public int MultiplexerPoolSize { get; init; } = 16;

        /// <summary>
        /// When true, scripts are reloaded when configuration changes.
        /// </summary>
        public bool HotReloadScripts { get; init; } = true;

        public void Validate()
        {
            if (string.IsNullOrWhiteSpace(ConnectionString))
            {
                throw new ValidationException("Redis connection string is required.");
            }

            if (string.IsNullOrWhiteSpace(KeyPrefix))
            {
                throw new ValidationException("Redis key prefix is required.");
            }

            if (KeyTtl <= TimeSpan.Zero)
            {
                throw new ValidationException("Redis key TTL must be positive.");
            }
        }
    }

    public sealed class PostgresOptions
    {
        [Required]
        public string ConnectionString { get; init; } = "Host=localhost;Username=postgres;Password=postgres;Database=rate_limiter";

        /// <summary>
        /// Schema used for policy and audit tables.
        /// </summary>
        [Required]
        public string Schema { get; init; } = "rate_limiter";

        /// <summary>
        /// Enables automatic migration scripts (idempotent create statements).
        /// </summary>
        public bool AutoMigrate { get; init; } = true;

        public void Validate()
        {
            if (string.IsNullOrWhiteSpace(ConnectionString))
            {
                throw new ValidationException("Postgres connection string is required.");
            }

            if (string.IsNullOrWhiteSpace(Schema))
            {
                throw new ValidationException("Postgres schema is required.");
            }
        }
    }

    public sealed class SlidingWindowOptions
    {
        [Range(typeof(TimeSpan), "00:00:05", "00:30:00")]
        public TimeSpan Window { get; init; } = TimeSpan.FromMinutes(1);

        [Range(2, 120)]
        public int Buckets { get; init; } = 30;

        public bool Enabled { get; init; } = true;

        public void Validate()
        {
            if (Window <= TimeSpan.Zero)
            {
                throw new ValidationException("Sliding window must be positive.");
            }

            if (Buckets <= 1)
            {
                throw new ValidationException("Sliding window bucket count must be greater than one.");
            }
        }
    }
}
