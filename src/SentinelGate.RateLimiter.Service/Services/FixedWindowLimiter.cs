using System.Collections.Concurrent;
using SentinelGate.Shared.Infrastructure.Redis;
using SentinelGate.Shared.Models.DTOs;
using SentinelGate.Shared.Models.Enums;

namespace SentinelGate.RateLimiter.Service.Services;

public class FixedWindowLimiter
{
    private readonly RedisConnectionManager _redis;
    private static readonly ConcurrentDictionary<string, (long Count, DateTime Expiry)> _fallback = new();

    public FixedWindowLimiter(RedisConnectionManager redis)
    {
        _redis = redis;
    }

    public async Task<RateLimitResult> CheckLimit(string clientKey, int limit, int windowSeconds)
    {
        var windowEpoch = DateTimeOffset.UtcNow.ToUnixTimeSeconds() / windowSeconds;
        var windowKey = $"rl:fw:{clientKey}:{windowEpoch}";
        var secondsIntoWindow = DateTimeOffset.UtcNow.ToUnixTimeSeconds() % windowSeconds;
        var resetAt = DateTime.UtcNow.AddSeconds(windowSeconds - secondsIntoWindow);

        var db = _redis.GetDatabase();
        if (db != null)
        {
            return await CheckLimitRedis(db, windowKey, limit, windowSeconds, clientKey, resetAt);
        }

        return CheckLimitInMemory(windowKey, limit, windowSeconds, clientKey, resetAt);
    }

    private static async Task<RateLimitResult> CheckLimitRedis(
        StackExchange.Redis.IDatabase db,
        string windowKey,
        int limit,
        int windowSeconds,
        string clientKey,
        DateTime resetAt)
    {
        var count = await db.StringIncrementAsync(windowKey);

        if (count == 1)
        {
            await db.KeyExpireAsync(windowKey, TimeSpan.FromSeconds(windowSeconds));
        }

        var remaining = Math.Max(0, limit - (int)count);
        var isAllowed = count <= limit;

        return new RateLimitResult(
            IsAllowed: isAllowed,
            Remaining: remaining,
            Limit: limit,
            ResetAt: resetAt,
            RetryAfter: isAllowed ? null : resetAt - DateTime.UtcNow,
            Algorithm: RateLimitAlgorithm.FixedWindow,
            ClientIdentity: clientKey
        );
    }

    private static RateLimitResult CheckLimitInMemory(
        string windowKey,
        int limit,
        int windowSeconds,
        string clientKey,
        DateTime resetAt)
    {
        // Clean up expired entries periodically
        CleanupExpired();

        var expiry = DateTime.UtcNow.AddSeconds(windowSeconds);
        var entry = _fallback.AddOrUpdate(
            windowKey,
            _ => (1, expiry),
            (_, existing) =>
            {
                if (existing.Expiry <= DateTime.UtcNow)
                    return (1, expiry);
                return (existing.Count + 1, existing.Expiry);
            }
        );

        var remaining = Math.Max(0, limit - (int)entry.Count);
        var isAllowed = entry.Count <= limit;

        return new RateLimitResult(
            IsAllowed: isAllowed,
            Remaining: remaining,
            Limit: limit,
            ResetAt: resetAt,
            RetryAfter: isAllowed ? null : resetAt - DateTime.UtcNow,
            Algorithm: RateLimitAlgorithm.FixedWindow,
            ClientIdentity: clientKey
        );
    }

    private static void CleanupExpired()
    {
        var now = DateTime.UtcNow;
        foreach (var key in _fallback.Keys)
        {
            if (_fallback.TryGetValue(key, out var entry) && entry.Expiry <= now)
            {
                _fallback.TryRemove(key, out _);
            }
        }
    }
}
