using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using DotNet.Testcontainers.Configurations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RateLimiter.Core.Abstractions;
using RateLimiter.Infrastructure;
using RateLimiter.Infrastructure.Configuration;
using RateLimiter.Infrastructure.Persistence;
using RateLimiter.Infrastructure.Services;
using Xunit;

namespace RateLimiter.IntegrationTests;

public class RateLimitPolicyCacheTests
{
    [Fact]
    public async Task InitializeAsync_MergesConfiguredAndPersistedPolicies()
    {
        var configured = new RateLimiterInfrastructureOptions.RateLimitPolicyConfiguration
        {
            PolicyName = "configured",
            PermitLimit = 10,
            Window = TimeSpan.FromSeconds(30),
            Precision = TimeSpan.FromMilliseconds(100)
        };

        var persisted = new RateLimitPolicy
        {
            PolicyName = "persisted",
            PermitLimit = 5,
            BurstLimit = 5,
            Window = TimeSpan.FromSeconds(10),
            Precision = TimeSpan.FromMilliseconds(50),
            SlidingWindowMetricsEnabled = true
        };

        var options = new RateLimiterInfrastructureOptions
        {
            WarmPoliciesOnStartup = true,
            Policies = new[] { configured }
        };

        var repository = new FakePolicyRepository(new[] { persisted });
        var monitor = new TestOptionsMonitor(options);
        var cache = new RateLimitPolicyCache(repository, monitor, NullLogger<RateLimitPolicyCache>.Instance);

        await cache.InitializeAsync();

        try
        {
            var configuredPolicy = cache.GetPolicy("configured");
            Assert.NotNull(configuredPolicy);
            Assert.Equal(configured.PermitLimit, configuredPolicy!.PermitLimit);

            var persistedPolicy = cache.GetPolicy("persisted");
            Assert.NotNull(persistedPolicy);
            Assert.Equal(persisted.PermitLimit, persistedPolicy!.PermitLimit);
        }
        finally
        {
            cache.Dispose();
        }
    }

    [Fact]
    public async Task OptionsChange_RefreshesPolicySet()
    {
        var initialOptions = new RateLimiterInfrastructureOptions
        {
            WarmPoliciesOnStartup = false,
            Policies = Array.Empty<RateLimiterInfrastructureOptions.RateLimitPolicyConfiguration>()
        };

        var repository = new FakePolicyRepository(Array.Empty<RateLimitPolicy>());
        var monitor = new TestOptionsMonitor(initialOptions);
        var cache = new RateLimitPolicyCache(repository, monitor, NullLogger<RateLimitPolicyCache>.Instance);

        await cache.InitializeAsync();

        var updatedOptions = new RateLimiterInfrastructureOptions
        {
            WarmPoliciesOnStartup = false,
            Policies = new[]
            {
                new RateLimiterInfrastructureOptions.RateLimitPolicyConfiguration
                {
                    PolicyName = "dynamic",
                    PermitLimit = 1,
                    Window = TimeSpan.FromSeconds(1)
                }
            }
        };

        monitor.Update(updatedOptions);

        try
        {
            await Task.Delay(100);
            var policy = cache.GetPolicy("dynamic");
            Assert.NotNull(policy);
        }
        finally
        {
            cache.Dispose();
        }
    }
}

[Collection("redis-postgres")]
public class DistributedRateLimiterIntegrationTests : IClassFixture<RedisPostgresFixture>
{
    private readonly RedisPostgresFixture _fixture;

    public DistributedRateLimiterIntegrationTests(RedisPostgresFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Should_EnforceRateLimitsAcrossRedisAndPostgres()
    {
        if (!_fixture.IsDockerAvailable)
        {
            return;
        }

        var keyPrefix = $"itest-{Guid.NewGuid():N}";

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["RateLimiter:Redis:ConnectionString"] = _fixture.RedisConnectionString,
                ["RateLimiter:Redis:KeyPrefix"] = keyPrefix,
                ["RateLimiter:Redis:MultiplexerPoolSize"] = "1",
                ["RateLimiter:Postgres:ConnectionString"] = _fixture.Postgres.ConnectionString,
                ["RateLimiter:Postgres:Schema"] = "public",
                ["RateLimiter:Postgres:AutoMigrate"] = "true",
                ["RateLimiter:WarmPoliciesOnStartup"] = "true"
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddRateLimiterInfrastructure(configuration);

        await using var provider = services.BuildServiceProvider();

        var repository = provider.GetRequiredService<IRateLimitPolicyRepository>();
        var policyName = $"policy-{Guid.NewGuid():N}";
        var policy = new RateLimitPolicy
        {
            PolicyName = policyName,
            PermitLimit = 3,
            BurstLimit = 3,
            Window = TimeSpan.FromSeconds(1),
            Precision = TimeSpan.FromMilliseconds(50),
            SlidingWindowMetricsEnabled = true
        };

        await repository.UpsertPolicyAsync(policy);

        var cache = provider.GetRequiredService<IRateLimitPolicyCache>();
        await cache.InitializeAsync();

        var loadedPolicy = cache.GetPolicy(policyName);
        Assert.NotNull(loadedPolicy);

        var limiter = provider.GetRequiredService<IRateLimiter>();
        var identity = new RateLimitIdentity("api-test", null, null, null);
        var request = new RateLimitRequest(loadedPolicy!, identity);

        for (var i = 0; i < policy.PermitLimit; i++)
        {
            var decision = await limiter.ShouldAllowAsync(request);
            Assert.True(decision.IsAllowed);
        }

        var blocked = await limiter.ShouldAllowAsync(request);
        Assert.False(blocked.IsAllowed);
        Assert.True(blocked.RetryAfter > TimeSpan.Zero);
    }
}

[CollectionDefinition("redis-postgres")]
public sealed class RedisPostgresCollection : ICollectionFixture<RedisPostgresFixture>
{
}

public sealed class RedisPostgresFixture : IAsyncLifetime
{
    private readonly RedisTestcontainer _redis;
    private readonly PostgreSqlTestcontainer _postgres;

    public bool IsDockerAvailable { get; private set; } = true;

    public string? SkipReason { get; private set; }

    public RedisPostgresFixture()
    {
        _redis = new TestcontainersBuilder<RedisTestcontainer>()
            .WithDatabase(new RedisTestcontainerConfiguration())
            .WithImage("redis:7-alpine")
            .Build();

        _postgres = new TestcontainersBuilder<PostgreSqlTestcontainer>()
            .WithDatabase(new PostgreSqlTestcontainerConfiguration
            {
                Database = "rate_limiter",
                Username = "postgres",
                Password = "postgres"
            })
            .WithImage("postgres:16-alpine")
            .Build();
    }

    public string RedisConnectionString
    {
        get
        {
            if (!IsDockerAvailable)
            {
                throw new InvalidOperationException("Docker is unavailable.");
            }

            return $"{_redis.Hostname}:{_redis.GetMappedPublicPort(6379)}";
        }
    }

    public PostgreSqlTestcontainer Postgres
    {
        get
        {
            if (!IsDockerAvailable)
            {
                throw new InvalidOperationException("Docker is unavailable.");
            }

            return _postgres;
        }
    }

    public async Task InitializeAsync()
    {
        try
        {
            await _redis.StartAsync();
            await _postgres.StartAsync();
        }
        catch (Exception ex)
        {
            IsDockerAvailable = false;
            SkipReason = $"Docker containers could not be started: {ex.Message}";
        }
    }

    public async Task DisposeAsync()
    {
        await SafeDisposeAsync(_redis);
        await SafeDisposeAsync(_postgres);
    }

    private static async Task SafeDisposeAsync(IAsyncDisposable resource)
    {
        try
        {
            await resource.DisposeAsync();
        }
        catch
        {
            // ignored
        }
    }
}

internal sealed class FakePolicyRepository : IRateLimitPolicyRepository
{
    private readonly List<RateLimitPolicy> _policies;

    public FakePolicyRepository(IEnumerable<RateLimitPolicy> policies)
    {
        _policies = new List<RateLimitPolicy>(policies);
    }

    public Task<IReadOnlyList<RateLimitPolicy>> GetPoliciesAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlyList<RateLimitPolicy> snapshot = _policies.ToList();
        return Task.FromResult(snapshot);
    }

    public Task UpsertPolicyAsync(RateLimitPolicy policy, CancellationToken cancellationToken = default)
    {
        var index = _policies.FindIndex(p => p.PolicyName == policy.PolicyName);
        if (index >= 0)
        {
            _policies[index] = policy;
        }
        else
        {
            _policies.Add(policy);
        }

        return Task.CompletedTask;
    }
}

internal sealed class TestOptionsMonitor : IOptionsMonitor<RateLimiterInfrastructureOptions>
{
    private RateLimiterInfrastructureOptions _current;
    private readonly List<Action<RateLimiterInfrastructureOptions, string?>> _listeners = new();

    public TestOptionsMonitor(RateLimiterInfrastructureOptions current)
    {
        _current = current;
    }

    public RateLimiterInfrastructureOptions CurrentValue => _current;

    public RateLimiterInfrastructureOptions Get(string? name) => _current;

    public IDisposable OnChange(Action<RateLimiterInfrastructureOptions, string?> listener)
    {
        _listeners.Add(listener);
        return new DisposableAction(() => _listeners.Remove(listener));
    }

    public void Update(RateLimiterInfrastructureOptions options, string? name = null)
    {
        _current = options;
        foreach (var listener in _listeners.ToArray())
        {
            listener(options, name ?? Options.DefaultName);
        }
    }
}

internal sealed class DisposableAction : IDisposable
{
    private readonly Action _dispose;
    private bool _disposed;

    public DisposableAction(Action dispose)
    {
        _dispose = dispose;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _dispose();
    }
}
