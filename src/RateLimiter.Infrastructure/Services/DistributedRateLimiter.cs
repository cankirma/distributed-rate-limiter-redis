using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RateLimiter.Core.Abstractions;
using RateLimiter.Core.Metrics;
using RateLimiter.Infrastructure.Configuration;
using RateLimiter.Infrastructure.Persistence;
using RateLimiter.Infrastructure.Redis;

namespace RateLimiter.Infrastructure.Services;

internal sealed class DistributedRateLimiter : IRateLimiter, IDisposable
{
    private static readonly ActivitySource ActivitySource = new("RateLimiter.Infrastructure");

    private readonly IRedisRateLimitStore _redisStore;
    private readonly IRateLimitPolicyCache _policyCache;
    private readonly IRateLimitAuditLogRepository _auditLogRepository;
    private readonly ILogger<DistributedRateLimiter> _logger;
    private readonly ConcurrentDictionary<string, SlidingWindowRateCounter> _counters = new(StringComparer.OrdinalIgnoreCase);
    private RateLimiterInfrastructureOptions _options;
    private readonly IDisposable? _changeRegistration;

    public DistributedRateLimiter(
        IRedisRateLimitStore redisStore,
        IRateLimitPolicyCache policyCache,
        IRateLimitAuditLogRepository auditLogRepository,
        IOptionsMonitor<RateLimiterInfrastructureOptions> optionsMonitor,
        ILogger<DistributedRateLimiter> logger)
    {
        _redisStore = redisStore;
        _policyCache = policyCache;
        _auditLogRepository = auditLogRepository;
        _logger = logger;
        _options = optionsMonitor.CurrentValue;
        _changeRegistration = optionsMonitor.OnChange(OnOptionsChanged);
    }

    public async ValueTask<RateLimitDecision> ShouldAllowAsync(RateLimitRequest request, CancellationToken cancellationToken = default)
    {
        using var activity = ActivitySource.StartActivity("RateLimiter.ShouldAllow", ActivityKind.Internal);
        activity?.SetTag("rate_limiter.policy", request.Policy.PolicyName);
        activity?.SetTag("rate_limiter.identity", request.Identity.MostSpecificComponent ?? "anonymous");

        var computation = await _redisStore.EvaluateAsync(request, cancellationToken).ConfigureAwait(false);
        var nowTicks = DateTimeOffset.UtcNow.Ticks;

        SlidingWindowSample sample = SlidingWindowSample.Empty;
        if (_options.SlidingWindow.Enabled && request.Policy.SlidingWindowMetricsEnabled)
        {
            var counter = _counters.GetOrAdd(request.Policy.PolicyName, policy =>
                new SlidingWindowRateCounter(_options.SlidingWindow.Window, _options.SlidingWindow.Buckets));
            sample = counter.AddSample(nowTicks, 1d);
        }

        var decision = new RateLimitDecision(
            computation.IsAllowed,
            computation.Counters,
            sample,
            computation.RetryAfter,
            nowTicks);

        if (!decision.IsAllowed)
        {
            activity?.SetStatus(ActivityStatusCode.Ok, "Rate limited");
            await PersistAuditLogAsync(request, decision, cancellationToken).ConfigureAwait(false);
            _logger.LogWarning(
                "Rate limit exceeded for {Policy} (identity: {Identity}). Remaining: {Remaining} RetryAfter: {RetryAfter}.",
                request.Policy.PolicyName,
                request.Identity.MostSpecificComponent ?? "anonymous",
                decision.Counters.Remaining,
                decision.RetryAfter);
        }
        else
        {
            activity?.SetStatus(ActivityStatusCode.Ok, "Allowed");
        }

        return decision;
    }

    private async Task PersistAuditLogAsync(RateLimitRequest request, RateLimitDecision decision, CancellationToken cancellationToken)
    {
        if (!_options.AuditLoggingEnabled)
        {
            return;
        }

        var entry = new RateLimitAuditLogEntry
        {
            PolicyName = request.Policy.PolicyName,
            IdentityComponent = request.Identity.MostSpecificComponent ?? "anonymous",
            Allowed = decision.IsAllowed,
            Limit = decision.Counters.Limit,
            Remaining = decision.Counters.RemainingAsInt(),
            RetryAfterMilliseconds = (int)decision.RetryAfter.TotalMilliseconds,
            OccurredAt = DateTimeOffset.UtcNow,
            EndpointPath = request.Policy.PolicyName,
            AdditionalData = null
        };

        try
        {
            await _auditLogRepository.InsertAsync(entry, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to persist rate limit audit log for {Policy}.", request.Policy.PolicyName);
        }
    }

    private void OnOptionsChanged(RateLimiterInfrastructureOptions options)
    {
        Interlocked.Exchange(ref _options, options);
    }

    public void Dispose()
    {
        foreach (var counter in _counters.Values)
        {
            counter.Dispose();
        }

        _changeRegistration?.Dispose();
        (_redisStore as IDisposable)?.Dispose();
    }
}
