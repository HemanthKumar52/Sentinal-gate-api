using System.Collections.Concurrent;
using SentinelGate.Shared.Infrastructure.Redis;
using SentinelGate.Shared.Models.DTOs;
using SentinelGate.Shared.Models.Enums;
using StackExchange.Redis;

namespace SentinelGate.RateLimiter.Service.Services;

public class SlidingWindowLimiter
{
    private readonly RedisConnectionManager _redis;

    private static readonly ConcurrentDictionary<string, List<long>> _fallbackWindows = new();
    private static readonly object _fallbackLock = new();

    /// <summary>
    /// Lua script that atomically:
    /// 1. Removes expired entries from the sorted set
    /// 2. Counts remaining entries in the window
    /// 3. Adds a new entry if under the limit
    /// Returns {isAllowed, remaining}
    /// </summary>
    private const string SlidingWindowLua = @"
        local key = KEYS[1]
        local now = tonumber(ARGV[1])
        local window_ms = tonumber(ARGV[2])
        local limit = tonumber(ARGV[3])
        local member = ARGV[4]

        -- Remove entries outside the window
        redis.call('ZREMRANGEBYSCORE', key, 0, now - window_ms)

        -- Count current entries in the window
        local count = redis.call('ZCARD', key)

        if count < limit then
            -- Add new entry with timestamp as score
            redis.call('ZADD', key, now, member)
            redis.call('PEXPIRE', key, window_ms)
            return { 1, limit - count - 1 }
        else
            redis.call('PEXPIRE', key, window_ms)
            return { 0, 0 }
        end
    ";

    public SlidingWindowLimiter(RedisConnectionManager redis)
    {
        _redis = redis;
    }

    public async Task<RateLimitResult> CheckLimit(string clientKey, int limit, int windowSeconds)
    {
        var windowKey = $"rl:sw:{clientKey}";
        var nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var windowMs = windowSeconds * 1000L;
        var resetAt = DateTime.UtcNow.AddSeconds(windowSeconds);

        var db = _redis.GetDatabase();
        if (db != null)
        {
            return await CheckLimitRedis(db, windowKey, nowMs, windowMs, limit, clientKey, resetAt);
        }

        return CheckLimitInMemory(windowKey, nowMs, windowMs, limit, windowSeconds, clientKey, resetAt);
    }

    private static async Task<RateLimitResult> CheckLimitRedis(
        IDatabase db,
        string windowKey,
        long nowMs,
        long windowMs,
        int limit,
        string clientKey,
        DateTime resetAt)
    {
        var uniqueMember = $"{nowMs}:{Guid.NewGuid():N}";

        var result = (RedisResult[]?)await db.ScriptEvaluateAsync(
            SlidingWindowLua,
            new RedisKey[] { windowKey },
            new RedisValue[] { nowMs, windowMs, limit, uniqueMember }
        );

        var isAllowed = (int)result![0] == 1;
        var remaining = (int)result[1];

        return new RateLimitResult(
            IsAllowed: isAllowed,
            Remaining: remaining,
            Limit: limit,
            ResetAt: resetAt,
            RetryAfter: isAllowed ? null : resetAt - DateTime.UtcNow,
            Algorithm: RateLimitAlgorithm.SlidingWindow,
            ClientIdentity: clientKey
        );
    }

    private static RateLimitResult CheckLimitInMemory(
        string windowKey,
        long nowMs,
        long windowMs,
        int limit,
        int windowSeconds,
        string clientKey,
        DateTime resetAt)
    {
        lock (_fallbackLock)
        {
            var timestamps = _fallbackWindows.GetOrAdd(windowKey, _ => new List<long>());

            // Remove expired entries
            var cutoff = nowMs - windowMs;
            timestamps.RemoveAll(t => t <= cutoff);

            if (timestamps.Count < limit)
            {
                timestamps.Add(nowMs);
                var remaining = limit - timestamps.Count;

                return new RateLimitResult(
                    IsAllowed: true,
                    Remaining: remaining,
                    Limit: limit,
                    ResetAt: resetAt,
                    RetryAfter: null,
                    Algorithm: RateLimitAlgorithm.SlidingWindow,
                    ClientIdentity: clientKey
                );
            }

            return new RateLimitResult(
                IsAllowed: false,
                Remaining: 0,
                Limit: limit,
                ResetAt: resetAt,
                RetryAfter: resetAt - DateTime.UtcNow,
                Algorithm: RateLimitAlgorithm.SlidingWindow,
                ClientIdentity: clientKey
            );
        }
    }
}
