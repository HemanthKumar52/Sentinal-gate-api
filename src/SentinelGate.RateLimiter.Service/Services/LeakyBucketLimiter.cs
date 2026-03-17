using System.Collections.Concurrent;
using SentinelGate.Shared.Infrastructure.Redis;
using SentinelGate.Shared.Models.DTOs;
using SentinelGate.Shared.Models.Enums;
using StackExchange.Redis;

namespace SentinelGate.RateLimiter.Service.Services;

public class LeakyBucketLimiter
{
    private readonly RedisConnectionManager _redis;

    private static readonly ConcurrentDictionary<string, (double Level, long LastLeakMs)> _fallback = new();
    private static readonly object _fallbackLock = new();

    /// <summary>
    /// Lua script for atomic leaky bucket operation:
    /// 1. Read current level and last leak timestamp from Redis hash
    /// 2. Calculate leakage since last update
    /// 3. Add request to bucket, check if overflowing
    /// 4. Update the hash with new state
    /// Returns {isAllowed, remaining}
    /// </summary>
    private const string LeakyBucketLua = @"
        local key = KEYS[1]
        local now = tonumber(ARGV[1])
        local capacity = tonumber(ARGV[2])
        local rate = tonumber(ARGV[3])

        local data = redis.call('HMGET', key, 'level', 'lastLeak')
        local level = tonumber(data[1]) or 0
        local lastLeak = tonumber(data[2]) or now

        -- Calculate leakage since last update
        local elapsed = (now - lastLeak) / 1000.0
        level = math.max(0, level - elapsed * rate)

        if level + 1 <= capacity then
            level = level + 1
            redis.call('HMSET', key, 'level', level, 'lastLeak', now)
            redis.call('EXPIRE', key, math.ceil(capacity / rate) + 1)
            return { 1, capacity - math.ceil(level) }
        else
            redis.call('HMSET', key, 'level', level, 'lastLeak', now)
            redis.call('EXPIRE', key, math.ceil(capacity / rate) + 1)
            return { 0, 0 }
        end
    ";

    public LeakyBucketLimiter(RedisConnectionManager redis)
    {
        _redis = redis;
    }

    public async Task<RateLimitResult> CheckLimit(string clientKey, int capacity, double leakRate)
    {
        var bucketKey = $"rl:lb:{clientKey}";
        var nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var retrySeconds = leakRate > 0 ? 1.0 / leakRate : 1.0;
        var resetAt = DateTime.UtcNow.AddSeconds(retrySeconds);

        var db = _redis.GetDatabase();
        if (db != null)
        {
            return await CheckLimitRedis(db, bucketKey, nowMs, capacity, leakRate, clientKey, resetAt, retrySeconds);
        }

        return CheckLimitInMemory(bucketKey, nowMs, capacity, leakRate, clientKey, resetAt, retrySeconds);
    }

    private static async Task<RateLimitResult> CheckLimitRedis(
        IDatabase db,
        string bucketKey,
        long nowMs,
        int capacity,
        double leakRate,
        string clientKey,
        DateTime resetAt,
        double retrySeconds)
    {
        var result = (RedisResult[]?)await db.ScriptEvaluateAsync(
            LeakyBucketLua,
            new RedisKey[] { bucketKey },
            new RedisValue[] { nowMs, capacity, leakRate }
        );

        var isAllowed = (int)result![0] == 1;
        var remaining = (int)result[1];

        return new RateLimitResult(
            IsAllowed: isAllowed,
            Remaining: remaining,
            Limit: capacity,
            ResetAt: resetAt,
            RetryAfter: isAllowed ? null : TimeSpan.FromSeconds(retrySeconds),
            Algorithm: RateLimitAlgorithm.LeakyBucket,
            ClientIdentity: clientKey
        );
    }

    private static RateLimitResult CheckLimitInMemory(
        string bucketKey,
        long nowMs,
        int capacity,
        double leakRate,
        string clientKey,
        DateTime resetAt,
        double retrySeconds)
    {
        lock (_fallbackLock)
        {
            var state = _fallback.GetOrAdd(bucketKey, _ => (0, nowMs));

            var level = state.Level;
            var lastLeak = state.LastLeakMs;

            // Calculate leakage since last update
            var elapsed = (nowMs - lastLeak) / 1000.0;
            level = Math.Max(0, level - elapsed * leakRate);

            bool isAllowed;
            if (level + 1 <= capacity)
            {
                level += 1;
                isAllowed = true;
            }
            else
            {
                isAllowed = false;
            }

            _fallback[bucketKey] = (level, nowMs);

            var remaining = Math.Max(0, capacity - (int)Math.Ceiling(level));

            return new RateLimitResult(
                IsAllowed: isAllowed,
                Remaining: remaining,
                Limit: capacity,
                ResetAt: resetAt,
                RetryAfter: isAllowed ? null : TimeSpan.FromSeconds(retrySeconds),
                Algorithm: RateLimitAlgorithm.LeakyBucket,
                ClientIdentity: clientKey
            );
        }
    }
}
