using SentinelGate.RateLimiter.Service.Services;
using SentinelGate.Shared.Models.DTOs;
using SentinelGate.Shared.Models.Enums;
using SentinelGate.Tests.Helpers;

namespace SentinelGate.Tests.RateLimiting;

public class FixedWindowLimiterTests : IDisposable
{
    private readonly TestFixture _fixture;
    private readonly FixedWindowLimiter _limiter;

    public FixedWindowLimiterTests()
    {
        _fixture = new TestFixture();
        _limiter = new FixedWindowLimiter(_fixture.RedisManager);
    }

    [Fact]
    public async Task Test_AllowsRequestsWithinLimit()
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
        Assert.Equal(limit, result.Limit);
        Assert.Equal(RateLimitAlgorithm.FixedWindow, result.Algorithm);
        Assert.Null(result.RetryAfter);
    }

    [Fact]
    public async Task Test_BlocksRequestsExceedingLimit()
    {
        // Arrange
        var clientKey = $"test-client-{Guid.NewGuid():N}";
        int limit = 3;
        int windowSeconds = 60;

        // Act - exhaust the limit
        for (int i = 0; i < limit; i++)
        {
            var allowed = await _limiter.CheckLimit(clientKey, limit, windowSeconds);
            Assert.True(allowed.IsAllowed, $"Request {i + 1} should be allowed");
        }

        // The next request should be blocked
        var result = await _limiter.CheckLimit(clientKey, limit, windowSeconds);

        // Assert
        Assert.False(result.IsAllowed);
        Assert.Equal(0, result.Remaining);
        Assert.NotNull(result.RetryAfter);
    }

    [Fact]
    public async Task Test_ResetsAfterWindow()
    {
        // Arrange - use a very short window to test the epoch-based windowing
        var clientKey = $"test-client-{Guid.NewGuid():N}";
        int limit = 2;
        int windowSeconds = 1;

        // Act - exhaust the limit
        for (int i = 0; i < limit; i++)
        {
            await _limiter.CheckLimit(clientKey, limit, windowSeconds);
        }

        var blocked = await _limiter.CheckLimit(clientKey, limit, windowSeconds);
        Assert.False(blocked.IsAllowed);

        // Wait for the window to pass
        await Task.Delay(1100);

        // Should be allowed again in the new window
        var result = await _limiter.CheckLimit(clientKey, limit, windowSeconds);

        // Assert
        Assert.True(result.IsAllowed);
    }

    [Theory]
    [InlineData(10, 1, 9)]
    [InlineData(10, 5, 5)]
    [InlineData(10, 10, 0)]
    [InlineData(10, 11, 0)]
    public async Task Test_ReturnsCorrectRemainingCount(int limit, int requestCount, int expectedRemaining)
    {
        // Arrange
        var clientKey = $"test-client-{Guid.NewGuid():N}";
        int windowSeconds = 60;

        // Act
        RateLimitResult? lastResult = null;
        for (int i = 0; i < requestCount; i++)
        {
            lastResult = await _limiter.CheckLimit(clientKey, limit, windowSeconds);
        }

        // Assert
        Assert.NotNull(lastResult);
        Assert.Equal(expectedRemaining, lastResult.Remaining);
    }

    public void Dispose()
    {
        _fixture.Dispose();
        GC.SuppressFinalize(this);
    }
}
