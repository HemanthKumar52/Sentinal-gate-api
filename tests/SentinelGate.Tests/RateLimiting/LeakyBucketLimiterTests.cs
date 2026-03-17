using SentinelGate.RateLimiter.Service.Services;
using SentinelGate.Shared.Models.Enums;
using SentinelGate.Tests.Helpers;

namespace SentinelGate.Tests.RateLimiting;

public class LeakyBucketLimiterTests : IDisposable
{
    private readonly TestFixture _fixture;
    private readonly LeakyBucketLimiter _limiter;

    public LeakyBucketLimiterTests()
    {
        _fixture = new TestFixture();
        _limiter = new LeakyBucketLimiter(_fixture.RedisManager);
    }

    [Fact]
    public async Task Test_AllowsWithinCapacity()
    {
        // Arrange
        var clientKey = $"test-client-{Guid.NewGuid():N}";
        int capacity = 5;
        double leakRate = 1.0;

        // Act
        var result = await _limiter.CheckLimit(clientKey, capacity, leakRate);

        // Assert
        Assert.True(result.IsAllowed);
        Assert.Equal(RateLimitAlgorithm.LeakyBucket, result.Algorithm);
        Assert.Equal(capacity, result.Limit);
        Assert.Null(result.RetryAfter);
    }

    [Fact]
    public async Task Test_BlocksWhenFull()
    {
        // Arrange
        var clientKey = $"test-client-{Guid.NewGuid():N}";
        int capacity = 3;
        double leakRate = 0.1; // Very slow leak so bucket fills up

        // Act - fill the bucket to capacity
        for (int i = 0; i < capacity; i++)
        {
            var allowed = await _limiter.CheckLimit(clientKey, capacity, leakRate);
            Assert.True(allowed.IsAllowed, $"Request {i + 1} should be allowed");
        }

        // Next request should overflow
        var result = await _limiter.CheckLimit(clientKey, capacity, leakRate);

        // Assert
        Assert.False(result.IsAllowed);
        Assert.Equal(0, result.Remaining);
        Assert.NotNull(result.RetryAfter);
    }

    [Fact]
    public async Task Test_LeaksOverTime()
    {
        // Arrange
        var clientKey = $"test-client-{Guid.NewGuid():N}";
        int capacity = 3;
        double leakRate = 5.0; // 5 units/sec - fast leak for testing

        // Act - fill the bucket
        for (int i = 0; i < capacity; i++)
        {
            await _limiter.CheckLimit(clientKey, capacity, leakRate);
        }

        // Should be full
        var full = await _limiter.CheckLimit(clientKey, capacity, leakRate);
        Assert.False(full.IsAllowed);

        // Wait for leakage (at 5/sec, 1 second should leak enough)
        await Task.Delay(1100);

        // Should have room again
        var result = await _limiter.CheckLimit(clientKey, capacity, leakRate);

        // Assert
        Assert.True(result.IsAllowed);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(5)]
    [InlineData(10)]
    public async Task Test_AllowsExactlyCapacityRequests(int capacity)
    {
        // Arrange
        var clientKey = $"test-client-{Guid.NewGuid():N}";
        double leakRate = 0.01; // Very slow leak

        // Act & Assert - all requests up to capacity should be allowed
        for (int i = 0; i < capacity; i++)
        {
            var result = await _limiter.CheckLimit(clientKey, capacity, leakRate);
            Assert.True(result.IsAllowed, $"Request {i + 1} of {capacity} should be allowed");
        }

        // One more should be blocked
        var overflow = await _limiter.CheckLimit(clientKey, capacity, leakRate);
        Assert.False(overflow.IsAllowed);
    }

    public void Dispose()
    {
        _fixture.Dispose();
        GC.SuppressFinalize(this);
    }
}
