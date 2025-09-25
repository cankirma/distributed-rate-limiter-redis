using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using RateLimiter.Core.Abstractions;
using RateLimiter.Infrastructure.Configuration;
using RateLimiter.Infrastructure.Persistence;
using RateLimiter.Infrastructure.Redis;
using RateLimiter.Infrastructure.Services;
using StackExchange.Redis;

namespace RateLimiter.Infrastructure;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddRateLimiterInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<RateLimiterInfrastructureOptions>()
            .Bind(configuration.GetSection(RateLimiterInfrastructureOptions.SectionName))
            .ValidateDataAnnotations()
            .Validate(options =>
            {
                try
                {
                    options.Validate();
                    return true;
                }
                catch
                {
                    return false;
                }
            }, "Invalid rate limiter infrastructure options.")
            .ValidateOnStart();

        services.AddSingleton<IConnectionMultiplexer>(sp =>
        {
            var options = sp.GetRequiredService<IOptions<RateLimiterInfrastructureOptions>>().Value;
            var configurationOptions = ConfigurationOptions.Parse(options.Redis.ConnectionString, true);
            if (options.Redis.Database.HasValue)
            {
                configurationOptions.DefaultDatabase = options.Redis.Database;
            }

            configurationOptions.AbortOnConnectFail = false;
            configurationOptions.ConnectRetry = 3;
            configurationOptions.KeepAlive = 30;
            return ConnectionMultiplexer.Connect(configurationOptions);
        });

        services.AddSingleton<IRateLimitPolicyRepository, RateLimitPolicyRepository>();
        services.AddSingleton<IRateLimitAuditLogRepository, RateLimitAuditLogRepository>();
        services.AddSingleton<IRedisRateLimitStore, RedisRateLimitStore>();
        services.AddSingleton<IRateLimitPolicyCache, RateLimitPolicyCache>();
        services.AddSingleton<IRateLimiter, DistributedRateLimiter>();
        services.AddHostedService<PolicyWarmupHostedService>();

        return services;
    }
}
