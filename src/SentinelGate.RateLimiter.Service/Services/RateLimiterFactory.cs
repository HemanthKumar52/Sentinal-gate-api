using SentinelGate.Shared.Models.DTOs;
using SentinelGate.Shared.Models.Enums;

namespace SentinelGate.RateLimiter.Service.Services;

public class RateLimiterFactory
{
    private readonly FixedWindowLimiter _fixedWindow;
    private readonly SlidingWindowLimiter _slidingWindow;
    private readonly TokenBucketLimiter _tokenBucket;
    private readonly LeakyBucketLimiter _leakyBucket;

    public RateLimiterFactory(
        FixedWindowLimiter fixedWindow,
        SlidingWindowLimiter slidingWindow,
        TokenBucketLimiter tokenBucket,
        LeakyBucketLimiter leakyBucket)
    {
        _fixedWindow = fixedWindow;
        _slidingWindow = slidingWindow;
        _tokenBucket = tokenBucket;
        _leakyBucket = leakyBucket;
    }

    /// <summary>
    /// Returns the appropriate rate limit result based on the chosen algorithm.
    /// </summary>
    public Task<RateLimitResult> CheckLimit(
        RateLimitAlgorithm algorithm,
        string clientKey,
        int limit,
        int windowSeconds,
        int? burstLimit = null,
        double? refillRate = null)
    {
        return algorithm switch
        {
            RateLimitAlgorithm.FixedWindow =>
                _fixedWindow.CheckLimit(clientKey, limit, windowSeconds),

            RateLimitAlgorithm.SlidingWindow =>
                _slidingWindow.CheckLimit(clientKey, limit, windowSeconds),

            RateLimitAlgorithm.TokenBucket =>
                _tokenBucket.CheckLimit(clientKey, burstLimit ?? limit, refillRate ?? 1.0),

            RateLimitAlgorithm.LeakyBucket =>
                _leakyBucket.CheckLimit(clientKey, burstLimit ?? limit, refillRate ?? 1.0),

            _ => throw new ArgumentOutOfRangeException(nameof(algorithm), algorithm,
                $"Unknown rate limit algorithm: {algorithm}")
        };
    }
}
