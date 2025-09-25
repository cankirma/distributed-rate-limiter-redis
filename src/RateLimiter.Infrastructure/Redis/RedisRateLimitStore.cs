using System.Globalization;
using System.Threading;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RateLimiter.Core.Abstractions;
using RateLimiter.Infrastructure.Configuration;
using StackExchange.Redis;

namespace RateLimiter.Infrastructure.Redis;

internal interface IRedisRateLimitStore
{
    Task<RateLimitComputationResult> EvaluateAsync(RateLimitRequest request, CancellationToken cancellationToken = default);
}

internal sealed class RedisRateLimitStore : IRedisRateLimitStore, IDisposable
{
    private readonly IConnectionMultiplexer _multiplexer;
    private readonly ILogger<RedisRateLimitStore> _logger;
    private RateLimiterInfrastructureOptions _options;
    private readonly IDisposable? _changeToken;

    private readonly string _tokenBucketScript;
    private readonly string _leakyBucketScript;

    public RedisRateLimitStore(
        IConnectionMultiplexer multiplexer,
        IOptionsMonitor<RateLimiterInfrastructureOptions> optionsMonitor,
        ILogger<RedisRateLimitStore> logger)
    {
        _multiplexer = multiplexer;
        _logger = logger;
        _options = optionsMonitor.CurrentValue;
        _tokenBucketScript = RedisScriptLoader.LoadScript("token_bucket.lua");
        _leakyBucketScript = RedisScriptLoader.LoadScript("leaky_bucket.lua");
        _changeToken = optionsMonitor.OnChange(OnOptionsChanged);
    }

    public async Task<RateLimitComputationResult> EvaluateAsync(RateLimitRequest request, CancellationToken cancellationToken = default)
    {
        var policy = request.Policy;
        var redisOptions = _options.Redis;
        var database = _multiplexer.GetDatabase(redisOptions.Database ?? -1);

        var key = ComposeKey(redisOptions.KeyPrefix, request);
        var nowTicks = DateTimeOffset.UtcNow.Ticks;
        var windowTicks = policy.Window.Ticks;
        var precisionTicks = policy.Precision.Ticks;
        var burstCapacity = policy.GetBurstCapacity();
        var tokensRequested = Math.Min((double)(request.Tokens * policy.TokensPerRequest), burstCapacity);
        var cooldownTicks = policy.Cooldown?.Ticks ?? 0L;
        var ttlSeconds = Math.Max(1, (int)redisOptions.KeyTtl.TotalSeconds);

        var script = policy.Algorithm == RateLimitAlgorithmType.TokenBucket ? _tokenBucketScript : _leakyBucketScript;

        var keys = new RedisKey[] { key };
        var args = new RedisValue[]
        {
            nowTicks,
            policy.PermitLimit,
            windowTicks,
            burstCapacity,
            precisionTicks,
            tokensRequested,
            ttlSeconds,
            cooldownTicks
        };

        try
        {
            var redisResult = await database.ScriptEvaluateAsync(script, keys, args).ConfigureAwait(false);
            if (redisResult.IsNull)
            {
                return RateLimitComputationResult.Allowed;
            }

            var result = (RedisResult[]?)redisResult;
            if (result is null)
            {
                return RateLimitComputationResult.Allowed;
            }

            return MapResult(policy, request, nowTicks, result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Redis rate limit evaluation failed for {Policy}.", policy.PolicyName);
            return new RateLimitComputationResult(true, new RateLimitCounters(policy.PermitLimit, policy.GetBurstCapacity(), 0d, policy.Window), TimeSpan.Zero, nowTicks);
        }
    }

    private RateLimitComputationResult MapResult(RateLimitPolicy policy, RateLimitRequest request, long nowTicks, RedisResult[] result)
    {
        if (result.Length < 6)
        {
            return RateLimitComputationResult.Allowed;
        }

    var allowed = (int)result[0] == 1;
    var stateValue = ParseDouble(result[1]);
    var retryAfterTicks = ParseLong(result[3]);
    var resetAfterTicks = ParseLong(result[4]);
    var usedThisCall = ParseDouble(result[5]);

        double remaining;
        double usedTotal;

        if (policy.Algorithm == RateLimitAlgorithmType.TokenBucket)
        {
            remaining = Math.Max(0d, stateValue);
            usedTotal = Math.Max(0d, policy.GetBurstCapacity() - remaining);
        }
        else
        {
            var waterLevel = Math.Max(0d, stateValue);
            remaining = Math.Max(0d, policy.GetBurstCapacity() - waterLevel);
            usedTotal = Math.Max(0d, policy.GetBurstCapacity() - remaining);
        }

        var counters = new RateLimitCounters(
            Limit: policy.PermitLimit,
            Remaining: remaining,
            Used: usedTotal,
            ResetAfter: TimeSpan.FromTicks(resetAfterTicks));

        var retryAfter = TimeSpan.FromTicks(retryAfterTicks);

        return new RateLimitComputationResult(allowed, counters, retryAfter, nowTicks);
    }

    private static RedisKey ComposeKey(string prefix, RateLimitRequest request)
    {
        var identityKey = request.Identity.ComposeStorageKey(request.Policy.PolicyName);
        return new RedisKey(string.Create(prefix.Length + 1 + identityKey.Length, (prefix, identityKey), static (span, state) =>
        {
            var (pfx, idKey) = state;
            pfx.AsSpan().CopyTo(span);
            var index = pfx.Length;
            span[index++] = ':';
            idKey.AsSpan().CopyTo(span[index..]);
        }));
    }

    private void OnOptionsChanged(RateLimiterInfrastructureOptions options)
    {
        Interlocked.Exchange(ref _options, options);
    }

    public void Dispose()
    {
        _changeToken?.Dispose();
    }

    private static double ParseDouble(RedisResult value)
        => double.Parse(value.ToString() ?? "0", CultureInfo.InvariantCulture);

    private static long ParseLong(RedisResult value)
        => long.Parse(value.ToString() ?? "0", CultureInfo.InvariantCulture);
}
