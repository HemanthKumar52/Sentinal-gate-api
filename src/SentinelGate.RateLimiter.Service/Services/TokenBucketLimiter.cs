using System.Collections.Concurrent;
using SentinelGate.Shared.Infrastructure.Redis;
using SentinelGate.Shared.Models.DTOs;
using SentinelGate.Shared.Models.Enums;
using StackExchange.Redis;

namespace SentinelGate.RateLimiter.Service.Services;

public class TokenBucketLimiter
{
    private readonly RedisConnectionManager _redis;

    private static readonly ConcurrentDictionary<string, (double Tokens, long LastRefillMs)> _fallback = new();
    private static readonly object _fallbackLock = new();

    /// <summary>
    /// Lua script for atomic token bucket operation:
    /// 1. Read current tokens and last refill timestamp from Redis hash
    /// 2. Calculate tokens to add based on elapsed time and refill rate
    /// 3. Consume one token if available
    /// 4. Update the hash with new state
    /// Returns {isAllowed, remaining}
    /// </summary>
    private const string TokenBucketLua = @"
        local key = KEYS[1]
        local now = tonumber(ARGV[1])
        local burst = tonumber(ARGV[2])
        local rate = tonumber(ARGV[3])

        local data = redis.call('HMGET', key, 'tokens', 'lastRefill')
        local tokens = tonumber(data[1]) or burst
        local lastRefill = tonumber(data[2]) or now

        -- Calculate tokens to add since last refill
        local elapsed = (now - lastRefill) / 1000.0
        tokens = math.min(burst, tokens + elapsed * rate)

        if tokens >= 1 then
            tokens = tokens - 1
            redis.call('HMSET', key, 'tokens', tokens, 'lastRefill', now)
            redis.call('EXPIRE', key, math.ceil(burst / rate) + 1)
            return { 1, math.floor(tokens) }
        else
            redis.call('HMSET', key, 'tokens', tokens, 'lastRefill', now)
            redis.call('EXPIRE', key, math.ceil(burst / rate) + 1)
            return { 0, 0 }
        end
    ";

    public TokenBucketLimiter(RedisConnectionManager redis)
    {
        _redis = redis;
    }

    public async Task<RateLimitResult> CheckLimit(string clientKey, int burstLimit, double refillRate)
    {
        var bucketKey = $"rl:tb:{clientKey}";
        var nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var retrySeconds = refillRate > 0 ? 1.0 / refillRate : 1.0;
        var resetAt = DateTime.UtcNow.AddSeconds(retrySeconds);

        var db = _redis.GetDatabase();
        if (db != null)
        {
            return await CheckLimitRedis(db, bucketKey, nowMs, burstLimit, refillRate, clientKey, resetAt, retrySeconds);
        }

        return CheckLimitInMemory(bucketKey, nowMs, burstLimit, refillRate, clientKey, resetAt, retrySeconds);
    }

    private static async Task<RateLimitResult> CheckLimitRedis(
        IDatabase db,
        string bucketKey,
        long nowMs,
        int burstLimit,
        double refillRate,
        string clientKey,
        DateTime resetAt,
        double retrySeconds)
    {
        var result = (RedisResult[]?)await db.ScriptEvaluateAsync(
            TokenBucketLua,
            new RedisKey[] { bucketKey },
            new RedisValue[] { nowMs, burstLimit, refillRate }
        );

        var isAllowed = (int)result![0] == 1;
        var remaining = (int)result[1];

        return new RateLimitResult(
            IsAllowed: isAllowed,
            Remaining: remaining,
            Limit: burstLimit,
            ResetAt: resetAt,
            RetryAfter: isAllowed ? null : TimeSpan.FromSeconds(retrySeconds),
            Algorithm: RateLimitAlgorithm.TokenBucket,
            ClientIdentity: clientKey
        );
    }

    private static RateLimitResult CheckLimitInMemory(
        string bucketKey,
        long nowMs,
        int burstLimit,
        double refillRate,
        string clientKey,
        DateTime resetAt,
        double retrySeconds)
    {
        lock (_fallbackLock)
        {
            var state = _fallback.GetOrAdd(bucketKey, _ => (burstLimit, nowMs));

            var tokens = state.Tokens;
            var lastRefill = state.LastRefillMs;

            // Calculate tokens to add since last refill
            var elapsed = (nowMs - lastRefill) / 1000.0;
            tokens = Math.Min(burstLimit, tokens + elapsed * refillRate);

            bool isAllowed;
            if (tokens >= 1)
            {
                tokens -= 1;
                isAllowed = true;
            }
            else
            {
                isAllowed = false;
            }

            _fallback[bucketKey] = (tokens, nowMs);

            var remaining = Math.Max(0, (int)tokens);

            return new RateLimitResult(
                IsAllowed: isAllowed,
                Remaining: remaining,
                Limit: burstLimit,
                ResetAt: resetAt,
                RetryAfter: isAllowed ? null : TimeSpan.FromSeconds(retrySeconds),
                Algorithm: RateLimitAlgorithm.TokenBucket,
                ClientIdentity: clientKey
            );
        }
    }
}
