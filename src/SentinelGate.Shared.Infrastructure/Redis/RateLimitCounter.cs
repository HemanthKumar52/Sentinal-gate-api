using SentinelGate.Shared.Models.DTOs;
using SentinelGate.Shared.Models.Enums;
using StackExchange.Redis;

namespace SentinelGate.Shared.Infrastructure.Redis;

public class RateLimitCounter
{
    private readonly RedisConnectionManager _redis;

    public RateLimitCounter(RedisConnectionManager redis)
    {
        _redis = redis;
    }

    // ─── Fixed Window ───────────────────────────────────────────────────

    public async Task<RateLimitResult> IncrementFixedWindow(string clientKey, int windowSeconds, int limit)
    {
        var windowKey = $"rl:fw:{clientKey}:{DateTimeOffset.UtcNow.ToUnixTimeSeconds() / windowSeconds}";
        var resetAt = DateTime.UtcNow.AddSeconds(windowSeconds - (DateTimeOffset.UtcNow.ToUnixTimeSeconds() % windowSeconds));

        var db = _redis.GetDatabase();
        if (db != null)
        {
            var count = await db.StringIncrementAsync(windowKey);
            if (count == 1)
                await db.KeyExpireAsync(windowKey, TimeSpan.FromSeconds(windowSeconds));

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

        // In-memory fallback
        return IncrementFixedWindowInMemory(windowKey, windowSeconds, limit, clientKey, resetAt);
    }

    private RateLimitResult IncrementFixedWindowInMemory(string windowKey, int windowSeconds, int limit, string clientKey, DateTime resetAt)
    {
        var fallback = _redis.InMemoryFallback;
        var expiry = DateTime.UtcNow.AddSeconds(windowSeconds);
        var entry = fallback.AddOrUpdate(
            windowKey,
            _ => (1, expiry),
            (_, existing) => (existing.Value + 1, existing.Expiry)
        );

        var remaining = Math.Max(0, limit - (int)entry.Value);
        var isAllowed = entry.Value <= limit;

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

    // ─── Sliding Window (Lua script for atomicity) ──────────────────────

    private const string SlidingWindowLuaScript = @"
        local key = KEYS[1]
        local now = tonumber(ARGV[1])
        local window = tonumber(ARGV[2])
        local limit = tonumber(ARGV[3])
        local member = ARGV[4]

        -- Remove expired entries
        redis.call('ZREMRANGEBYSCORE', key, 0, now - window)

        -- Count current entries
        local count = redis.call('ZCARD', key)

        if count < limit then
            -- Add new entry
            redis.call('ZADD', key, now, member)
            redis.call('EXPIRE', key, window)
            return { 1, limit - count - 1 }
        else
            redis.call('EXPIRE', key, window)
            return { 0, 0 }
        end
    ";

    public async Task<RateLimitResult> IncrementSlidingWindow(string clientKey, int windowSeconds, int limit)
    {
        var windowKey = $"rl:sw:{clientKey}";
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var windowMs = windowSeconds * 1000L;
        var resetAt = DateTime.UtcNow.AddSeconds(windowSeconds);

        var db = _redis.GetDatabase();
        if (db != null)
        {
            var result = (RedisResult[]?)await db.ScriptEvaluateAsync(
                SlidingWindowLuaScript,
                new RedisKey[] { windowKey },
                new RedisValue[] { now, windowMs, limit, $"{now}:{Guid.NewGuid():N}" }
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

        // In-memory fallback
        return IncrementSlidingWindowInMemory(clientKey, windowSeconds, limit, resetAt);
    }

    private RateLimitResult IncrementSlidingWindowInMemory(string clientKey, int windowSeconds, int limit, DateTime resetAt)
    {
        var fallback = _redis.InMemoryFallback;
        var windowKey = $"rl:sw:mem:{clientKey}";
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var windowMs = windowSeconds * 1000L;

        // Use a simple counter with expiry for in-memory sliding window approximation
        var expiry = DateTime.UtcNow.AddSeconds(windowSeconds);
        var entry = fallback.AddOrUpdate(
            windowKey,
            _ => (1, expiry),
            (_, existing) =>
            {
                if (existing.Expiry <= DateTime.UtcNow)
                    return (1, expiry);
                return (existing.Value + 1, existing.Expiry);
            }
        );

        var remaining = Math.Max(0, limit - (int)entry.Value);
        var isAllowed = entry.Value <= limit;

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

    // ─── Token Bucket ───────────────────────────────────────────────────

    public async Task<RateLimitResult> IncrementTokenBucket(string clientKey, int burstLimit, double refillRate)
    {
        var bucketKey = $"rl:tb:{clientKey}";
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var resetAt = DateTime.UtcNow.AddSeconds(1.0 / refillRate);

        var db = _redis.GetDatabase();
        if (db != null)
        {
            var tokenScript = @"
                local key = KEYS[1]
                local now = tonumber(ARGV[1])
                local burst = tonumber(ARGV[2])
                local rate = tonumber(ARGV[3])

                local data = redis.call('HMGET', key, 'tokens', 'last')
                local tokens = tonumber(data[1]) or burst
                local last = tonumber(data[2]) or now

                -- Calculate tokens to add based on elapsed time
                local elapsed = (now - last) / 1000.0
                tokens = math.min(burst, tokens + elapsed * rate)

                if tokens >= 1 then
                    tokens = tokens - 1
                    redis.call('HMSET', key, 'tokens', tokens, 'last', now)
                    redis.call('EXPIRE', key, math.ceil(burst / rate) + 1)
                    return { 1, math.floor(tokens) }
                else
                    redis.call('HMSET', key, 'tokens', tokens, 'last', now)
                    redis.call('EXPIRE', key, math.ceil(burst / rate) + 1)
                    return { 0, 0 }
                end
            ";

            var result = (RedisResult[]?)await db.ScriptEvaluateAsync(
                tokenScript,
                new RedisKey[] { bucketKey },
                new RedisValue[] { now, burstLimit, refillRate }
            );

            var isAllowed = (int)result![0] == 1;
            var remaining = (int)result[1];

            return new RateLimitResult(
                IsAllowed: isAllowed,
                Remaining: remaining,
                Limit: burstLimit,
                ResetAt: resetAt,
                RetryAfter: isAllowed ? null : TimeSpan.FromSeconds(1.0 / refillRate),
                Algorithm: RateLimitAlgorithm.TokenBucket,
                ClientIdentity: clientKey
            );
        }

        // In-memory fallback
        return IncrementTokenBucketInMemory(clientKey, burstLimit, refillRate, resetAt);
    }

    private RateLimitResult IncrementTokenBucketInMemory(string clientKey, int burstLimit, double refillRate, DateTime resetAt)
    {
        var fallback = _redis.InMemoryFallback;
        var tokensKey = $"rl:tb:tokens:{clientKey}";
        var lastKey = $"rl:tb:last:{clientKey}";
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        // Get last known state
        fallback.TryGetValue(lastKey, out var lastEntry);
        fallback.TryGetValue(tokensKey, out var tokensEntry);

        var lastTime = lastEntry.Value > 0 ? lastEntry.Value : now;
        double tokens = tokensEntry.Value > 0 ? tokensEntry.Value : burstLimit;

        // Refill tokens
        var elapsed = (now - lastTime) / 1000.0;
        tokens = Math.Min(burstLimit, tokens + elapsed * refillRate);

        var farExpiry = DateTime.UtcNow.AddSeconds(Math.Ceiling(burstLimit / refillRate) + 1);
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

        fallback[tokensKey] = ((long)tokens, farExpiry);
        fallback[lastKey] = (now, farExpiry);

        var remaining = Math.Max(0, (int)tokens);

        return new RateLimitResult(
            IsAllowed: isAllowed,
            Remaining: remaining,
            Limit: burstLimit,
            ResetAt: resetAt,
            RetryAfter: isAllowed ? null : TimeSpan.FromSeconds(1.0 / refillRate),
            Algorithm: RateLimitAlgorithm.TokenBucket,
            ClientIdentity: clientKey
        );
    }

    // ─── Leaky Bucket ───────────────────────────────────────────────────

    public async Task<RateLimitResult> IncrementLeakyBucket(string clientKey, int capacity, double leakRate)
    {
        var bucketKey = $"rl:lb:{clientKey}";
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var resetAt = DateTime.UtcNow.AddSeconds(1.0 / leakRate);

        var db = _redis.GetDatabase();
        if (db != null)
        {
            var leakyScript = @"
                local key = KEYS[1]
                local now = tonumber(ARGV[1])
                local capacity = tonumber(ARGV[2])
                local rate = tonumber(ARGV[3])

                local data = redis.call('HMGET', key, 'level', 'last')
                local level = tonumber(data[1]) or 0
                local last = tonumber(data[2]) or now

                -- Leak based on elapsed time
                local elapsed = (now - last) / 1000.0
                level = math.max(0, level - elapsed * rate)

                if level + 1 <= capacity then
                    level = level + 1
                    redis.call('HMSET', key, 'level', level, 'last', now)
                    redis.call('EXPIRE', key, math.ceil(capacity / rate) + 1)
                    return { 1, capacity - math.ceil(level) }
                else
                    redis.call('HMSET', key, 'level', level, 'last', now)
                    redis.call('EXPIRE', key, math.ceil(capacity / rate) + 1)
                    return { 0, 0 }
                end
            ";

            var result = (RedisResult[]?)await db.ScriptEvaluateAsync(
                leakyScript,
                new RedisKey[] { bucketKey },
                new RedisValue[] { now, capacity, leakRate }
            );

            var isAllowed = (int)result![0] == 1;
            var remaining = (int)result[1];

            return new RateLimitResult(
                IsAllowed: isAllowed,
                Remaining: remaining,
                Limit: capacity,
                ResetAt: resetAt,
                RetryAfter: isAllowed ? null : TimeSpan.FromSeconds(1.0 / leakRate),
                Algorithm: RateLimitAlgorithm.LeakyBucket,
                ClientIdentity: clientKey
            );
        }

        // In-memory fallback
        return IncrementLeakyBucketInMemory(clientKey, capacity, leakRate, resetAt);
    }

    private RateLimitResult IncrementLeakyBucketInMemory(string clientKey, int capacity, double leakRate, DateTime resetAt)
    {
        var fallback = _redis.InMemoryFallback;
        var levelKey = $"rl:lb:level:{clientKey}";
        var lastKey = $"rl:lb:last:{clientKey}";
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        fallback.TryGetValue(lastKey, out var lastEntry);
        fallback.TryGetValue(levelKey, out var levelEntry);

        var lastTime = lastEntry.Value > 0 ? lastEntry.Value : now;
        double level = levelEntry.Value;

        // Leak
        var elapsed = (now - lastTime) / 1000.0;
        level = Math.Max(0, level - elapsed * leakRate);

        var farExpiry = DateTime.UtcNow.AddSeconds(Math.Ceiling(capacity / leakRate) + 1);
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

        fallback[levelKey] = ((long)Math.Ceiling(level), farExpiry);
        fallback[lastKey] = (now, farExpiry);

        var remaining = Math.Max(0, capacity - (int)Math.Ceiling(level));

        return new RateLimitResult(
            IsAllowed: isAllowed,
            Remaining: remaining,
            Limit: capacity,
            ResetAt: resetAt,
            RetryAfter: isAllowed ? null : TimeSpan.FromSeconds(1.0 / leakRate),
            Algorithm: RateLimitAlgorithm.LeakyBucket,
            ClientIdentity: clientKey
        );
    }
}
