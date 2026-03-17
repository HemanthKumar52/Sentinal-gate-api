using SentinelGate.RateLimiter.Service.Services;
using SentinelGate.Shared.Models.Enums;
using SentinelGate.Tests.Helpers;

namespace SentinelGate.Tests.RateLimiting;

public class SlidingWindowLimiterTests : IDisposable
{
    private readonly TestFixture _fixture;
    private readonly SlidingWindowLimiter _limiter;

    public SlidingWindowLimiterTests()
    {
        _fixture = new TestFixture();
        _limiter = new SlidingWindowLimiter(_fixture.RedisManager);
    }

    [Fact]
    public async Task Test_AllowsRequestsWithinWindow()
    {
        // Arrange
        var clientKey = $"test-client-{Guid.NewGuid():N}";
        int limit = 5;
        int windowSeconds = 60;

        // Act
        var result = await _limiter.CheckLimit(clientKey, limit, windowSeconds);

        // Assert
        Assert.True(result.IsAllowed);
        Assert.Equal(limit - 1, result.Remaining);
        Assert.Equal(RateLimitAlgorithm.SlidingWindow, result.Algorithm);
        Assert.Null(result.RetryAfter);
    }

    [Fact]
    public async Task Test_BlocksWhenWindowFull()
    {
        // Arrange
        var clientKey = $"test-client-{Guid.NewGuid():N}";
        int limit = 3;
        int windowSeconds = 60;

        // Act - fill the window
        for (int i = 0; i < limit; i++)
        {
            var allowed = await _limiter.CheckLimit(clientKey, limit, windowSeconds);
            Assert.True(allowed.IsAllowed, $"Request {i + 1} should be allowed");
        }

        // Next request should be blocked
        var result = await _limiter.CheckLimit(clientKey, limit, windowSeconds);

        // Assert
        Assert.False(result.IsAllowed);
        Assert.Equal(0, result.Remaining);
        Assert.NotNull(result.RetryAfter);
    }

    [Fact]
    public async Task Test_SlidesCorrectly()
    {
        // Arrange - use a short window to verify sliding behavior
        var clientKey = $"test-client-{Guid.NewGuid():N}";
        int limit = 2;
        int windowSeconds = 1;

        // Act - fill the window
        for (int i = 0; i < limit; i++)
        {
            await _limiter.CheckLimit(clientKey, limit, windowSeconds);
        }

        // Should be blocked now
        var blocked = await _limiter.CheckLimit(clientKey, limit, windowSeconds);
        Assert.False(blocked.IsAllowed);

        // Wait for entries to slide out of the window
        await Task.Delay(1100);

        // Should be allowed again as old entries have expired
        var result = await _limiter.CheckLimit(clientKey, limit, windowSeconds);

        // Assert
        Assert.True(result.IsAllowed);
        Assert.Equal(limit - 1, result.Remaining);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(5)]
    [InlineData(10)]
    public async Task Test_AllowsExactlyLimitRequests(int limit)
    {
        // Arrange
        var clientKey = $"test-client-{Guid.NewGuid():N}";
        int windowSeconds = 60;

        // Act & Assert - all requests up to limit should be allowed
        for (int i = 0; i < limit; i++)
        {
            var result = await _limiter.CheckLimit(clientKey, limit, windowSeconds);
            Assert.True(result.IsAllowed, $"Request {i + 1} of {limit} should be allowed");
        }

        // One more should be blocked
        var finalResult = await _limiter.CheckLimit(clientKey, limit, windowSeconds);
        Assert.False(finalResult.IsAllowed);
    }

    public void Dispose()
    {
        _fixture.Dispose();
        GC.SuppressFinalize(this);
    }
}
