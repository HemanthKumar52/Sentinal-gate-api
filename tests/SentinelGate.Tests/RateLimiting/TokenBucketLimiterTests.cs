using SentinelGate.RateLimiter.Service.Services;
using SentinelGate.Shared.Models.Enums;
using SentinelGate.Tests.Helpers;

namespace SentinelGate.Tests.RateLimiting;

public class TokenBucketLimiterTests : IDisposable
{
    private readonly TestFixture _fixture;
    private readonly TokenBucketLimiter _limiter;

    public TokenBucketLimiterTests()
    {
        _fixture = new TestFixture();
        _limiter = new TokenBucketLimiter(_fixture.RedisManager);
    }

    [Fact]
    public async Task Test_AllowsBurstUpToLimit()
    {
        // Arrange
        var clientKey = $"test-client-{Guid.NewGuid():N}";
        int burstLimit = 5;
        double refillRate = 1.0; // 1 token/sec

        // Act - consume all tokens in a burst
        for (int i = 0; i < burstLimit; i++)
        {
            var result = await _limiter.CheckLimit(clientKey, burstLimit, refillRate);
            Assert.True(result.IsAllowed, $"Burst request {i + 1} should be allowed");
            Assert.Equal(RateLimitAlgorithm.TokenBucket, result.Algorithm);
        }

        // Next request should be blocked (bucket empty)
        var blocked = await _limiter.CheckLimit(clientKey, burstLimit, refillRate);

        // Assert
        Assert.False(blocked.IsAllowed);
        Assert.Equal(0, blocked.Remaining);
        Assert.NotNull(blocked.RetryAfter);
    }

    [Fact]
    public async Task Test_RefillsOverTime()
    {
        // Arrange
        var clientKey = $"test-client-{Guid.NewGuid():N}";
        int burstLimit = 3;
        double refillRate = 5.0; // 5 tokens/sec - fast refill for testing

        // Act - drain the bucket
        for (int i = 0; i < burstLimit; i++)
        {
            await _limiter.CheckLimit(clientKey, burstLimit, refillRate);
        }

        // Should be empty
        var empty = await _limiter.CheckLimit(clientKey, burstLimit, refillRate);
        Assert.False(empty.IsAllowed);

        // Wait for refill (at 5 tokens/sec, 1 second should refill the bucket)
        await Task.Delay(1100);

        // Should have tokens again
        var result = await _limiter.CheckLimit(clientKey, burstLimit, refillRate);

        // Assert
        Assert.True(result.IsAllowed);
    }

    [Fact]
    public async Task Test_BlocksWhenEmpty()
    {
        // Arrange
        var clientKey = $"test-client-{Guid.NewGuid():N}";
        int burstLimit = 2;
        double refillRate = 0.1; // Very slow refill

        // Act - drain the bucket
        for (int i = 0; i < burstLimit; i++)
        {
            await _limiter.CheckLimit(clientKey, burstLimit, refillRate);
        }

        // Assert - multiple subsequent requests should all be blocked
        for (int i = 0; i < 3; i++)
        {
            var result = await _limiter.CheckLimit(clientKey, burstLimit, refillRate);
            Assert.False(result.IsAllowed, $"Request after drain {i + 1} should be blocked");
            Assert.Equal(0, result.Remaining);
            Assert.Equal(burstLimit, result.Limit);
        }
    }

    [Theory]
    [InlineData(1, 1.0)]
    [InlineData(10, 5.0)]
    [InlineData(20, 10.0)]
    public async Task Test_FirstRequestAlwaysAllowed(int burstLimit, double refillRate)
    {
        // Arrange
        var clientKey = $"test-client-{Guid.NewGuid():N}";

        // Act
        var result = await _limiter.CheckLimit(clientKey, burstLimit, refillRate);

        // Assert
        Assert.True(result.IsAllowed);
        Assert.Equal(burstLimit - 1, result.Remaining);
    }

    public void Dispose()
    {
        _fixture.Dispose();
        GC.SuppressFinalize(this);
    }
}
