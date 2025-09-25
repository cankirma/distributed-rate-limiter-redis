using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RateLimiter.Core.Abstractions;
using RateLimiter.Infrastructure.Configuration;
using RateLimiter.Infrastructure.Persistence;

namespace RateLimiter.Infrastructure.Services;

internal interface IRateLimitPolicyCache : IDisposable
{
    ValueTask InitializeAsync(CancellationToken cancellationToken = default);

    RateLimitPolicy? GetPolicy(string policyName);

    IReadOnlyDictionary<string, RateLimitPolicy> SnapshotPolicies();
}

internal sealed class RateLimitPolicyCache : IRateLimitPolicyCache
{
    private readonly IRateLimitPolicyRepository _repository;
    private readonly ILogger<RateLimitPolicyCache> _logger;
    private readonly SemaphoreSlim _refreshLock = new(1, 1);
    private readonly IDisposable? _changeRegistration;
    private Timer? _timer;
    private RateLimiterInfrastructureOptions _options;
    private Dictionary<string, RateLimitPolicy> _policies = new(StringComparer.OrdinalIgnoreCase);

    public RateLimitPolicyCache(
        IRateLimitPolicyRepository repository,
        IOptionsMonitor<RateLimiterInfrastructureOptions> optionsMonitor,
        ILogger<RateLimitPolicyCache> logger)
    {
        _repository = repository;
        _logger = logger;
        _options = optionsMonitor.CurrentValue;
        _changeRegistration = optionsMonitor.OnChange(OnOptionsChanged);
    }

    public async ValueTask InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (_options.WarmPoliciesOnStartup)
        {
            await RefreshAsync(cancellationToken).ConfigureAwait(false);
        }
        ConfigureTimer();
    }

    public RateLimitPolicy? GetPolicy(string policyName)
    {
        if (_policies.TryGetValue(policyName, out var policy))
        {
            return policy;
        }

        return null;
    }

    public IReadOnlyDictionary<string, RateLimitPolicy> SnapshotPolicies()
        => Volatile.Read(ref _policies);

    private async Task RefreshAsync(CancellationToken cancellationToken)
    {
        if (!await _refreshLock.WaitAsync(0, cancellationToken).ConfigureAwait(false))
        {
            return;
        }

        try
        {
            var policies = await _repository.GetPoliciesAsync(cancellationToken).ConfigureAwait(false);
            var map = new Dictionary<string, RateLimitPolicy>(StringComparer.OrdinalIgnoreCase);
            foreach (var policy in policies)
            {
                map[policy.PolicyName] = policy;
            }

            Volatile.Write(ref _policies, map);
            _logger.LogInformation("Loaded {PolicyCount} rate limit policies from persistence.", map.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to refresh rate limit policies.");
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    private void ConfigureTimer()
    {
        _timer?.Dispose();
        if (_options.PolicyReloadInterval <= TimeSpan.Zero)
        {
            return;
        }

        _timer = new Timer(static state =>
        {
            if (state is RateLimitPolicyCache cache)
            {
                _ = Task.Run(async () => await cache.RefreshAsync(CancellationToken.None).ConfigureAwait(false));
            }
        }, this, _options.PolicyReloadInterval, _options.PolicyReloadInterval);
    }

    private void OnOptionsChanged(RateLimiterInfrastructureOptions options)
    {
        Interlocked.Exchange(ref _options, options);
        ConfigureTimer();
    }

    public void Dispose()
    {
        _timer?.Dispose();
        _changeRegistration?.Dispose();
        _refreshLock.Dispose();
    }
}
