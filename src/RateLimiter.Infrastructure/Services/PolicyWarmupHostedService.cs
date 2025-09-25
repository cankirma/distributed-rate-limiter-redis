using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RateLimiter.Infrastructure.Configuration;

namespace RateLimiter.Infrastructure.Services;

internal sealed class PolicyWarmupHostedService : IHostedService
{
    private readonly IRateLimitPolicyCache _policyCache;
    private readonly IOptionsMonitor<RateLimiterInfrastructureOptions> _optionsMonitor;
    private readonly ILogger<PolicyWarmupHostedService> _logger;

    public PolicyWarmupHostedService(
        IRateLimitPolicyCache policyCache,
        IOptionsMonitor<RateLimiterInfrastructureOptions> optionsMonitor,
        ILogger<PolicyWarmupHostedService> logger)
    {
        _policyCache = policyCache;
        _optionsMonitor = optionsMonitor;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var warmupEnabled = _optionsMonitor.CurrentValue.WarmPoliciesOnStartup;
        if (warmupEnabled)
        {
            _logger.LogInformation("Pre-loading rate limit policies from persistence.");
            await _policyCache.InitializeAsync(cancellationToken).ConfigureAwait(false);
        }
        else
        {
            _logger.LogInformation("Policy warmup disabled. Initializing cache without eager load.");
            await _policyCache.InitializeAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}
