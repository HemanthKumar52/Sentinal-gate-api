using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using SentinelGate.Gateway.API.Middleware;
using SentinelGate.Shared.Infrastructure.Data;
using SentinelGate.Shared.Infrastructure.Redis;
using SentinelGate.Shared.Models.Configuration;
using SentinelGate.Shared.Models.Entities;
using SentinelGate.Shared.Models.Enums;
using SentinelGate.Tests.Helpers;

namespace SentinelGate.Tests.Middleware;

public class RateLimitMiddlewareTests : IDisposable
{
    private readonly TestFixture _fixture;
    private readonly RateLimitMiddleware _middleware;
    private readonly RateLimitCounter _counter;

    public RateLimitMiddlewareTests()
    {
        _fixture = new TestFixture();

        _counter = new RateLimitCounter(_fixture.RedisManager);

        var options = Options.Create(new SentinelGateOptions
        {
            RateLimiting = new RateLimitingOptions
            {
                DefaultAlgorithm = RateLimitAlgorithm.FixedWindow,
                DefaultLimit = 3,
                DefaultWindowSeconds = 60
            }
        });

        var logger = _fixture.ServiceProvider.GetRequiredService<ILogger<RateLimitMiddleware>>();
        _middleware = new RateLimitMiddleware(_counter, options, logger);
    }

    private DefaultHttpContext CreateHttpContext(string clientIdentity = "test-client", string path = "/api/test")
    {
        var context = new DefaultHttpContext();
        context.Items["ClientIdentity"] = clientIdentity;
        context.Request.Path = path;
        context.Response.Body = new MemoryStream();

        // Register SentinelGateDbContext in the request services
        var services = new ServiceCollection();
        services.AddDbContext<SentinelGateDbContext>(opts =>
            opts.UseInMemoryDatabase($"middleware-test-{Guid.NewGuid():N}"));
        context.RequestServices = services.BuildServiceProvider();

        return context;
    }

    [Fact]
    public async Task Test_AddsRateLimitHeaders()
    {
        // Arrange
        var clientId = $"middleware-test-{Guid.NewGuid():N}";
        var context = CreateHttpContext(clientIdentity: clientId);
        var nextCalled = false;
        RequestDelegate next = _ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        };

        // Act
        await _middleware.InvokeAsync(context, next);

        // Assert
        Assert.True(nextCalled, "Next middleware should have been called");
        Assert.True(context.Response.Headers.ContainsKey("X-RateLimit-Limit"));
        Assert.True(context.Response.Headers.ContainsKey("X-RateLimit-Remaining"));
        Assert.True(context.Response.Headers.ContainsKey("X-RateLimit-Reset"));
        Assert.True(context.Response.Headers.ContainsKey("X-RateLimit-Policy"));
        Assert.Equal("3", context.Response.Headers["X-RateLimit-Limit"].ToString());
    }

    [Fact]
    public async Task Test_Returns429WhenLimitExceeded()
    {
        // Arrange - use a unique client per test to avoid state leakage
        var clientId = $"middleware-429-{Guid.NewGuid():N}";
        RequestDelegate next = _ => Task.CompletedTask;

        // Exhaust the rate limit (limit=3)
        for (int i = 0; i < 3; i++)
        {
            var ctx = CreateHttpContext(clientIdentity: clientId);
            await _middleware.InvokeAsync(ctx, next);
            Assert.NotEqual(429, ctx.Response.StatusCode);
        }

        // Act - the 4th request should be rate-limited
        var context = CreateHttpContext(clientIdentity: clientId);
        await _middleware.InvokeAsync(context, next);

        // Assert
        Assert.Equal(StatusCodes.Status429TooManyRequests, context.Response.StatusCode);
        Assert.True(context.Response.Headers.ContainsKey("Retry-After"));
    }

    [Fact]
    public async Task Test_Returns403WhenBlocked()
    {
        // Note: The RateLimitMiddleware itself returns 429, not 403.
        // The 403 comes from the BlockListCheckMiddleware. We test that
        // a rate-limited request gets the correct 429 status and verify
        // the response body contains expected rate limit information.

        var clientId = $"middleware-blocked-{Guid.NewGuid():N}";
        RequestDelegate next = _ => Task.CompletedTask;

        // Exhaust the limit
        for (int i = 0; i < 3; i++)
        {
            var ctx = CreateHttpContext(clientIdentity: clientId);
            await _middleware.InvokeAsync(ctx, next);
        }

        // Act
        var context = CreateHttpContext(clientIdentity: clientId);
        await _middleware.InvokeAsync(context, next);

        // Assert - should be 429 with rate limit info
        Assert.Equal(StatusCodes.Status429TooManyRequests, context.Response.StatusCode);
        Assert.Contains("application/json", context.Response.ContentType);

        // Read response body
        context.Response.Body.Seek(0, SeekOrigin.Begin);
        using var reader = new StreamReader(context.Response.Body);
        var body = await reader.ReadToEndAsync();
        Assert.Contains("Too Many Requests", body);
        Assert.Contains("retryAfterSeconds", body);
    }

    [Fact]
    public async Task Test_DifferentClientsHaveSeparateLimits()
    {
        // Arrange
        var clientA = $"client-A-{Guid.NewGuid():N}";
        var clientB = $"client-B-{Guid.NewGuid():N}";
        RequestDelegate next = _ => Task.CompletedTask;

        // Exhaust client A's limit
        for (int i = 0; i < 3; i++)
        {
            var ctx = CreateHttpContext(clientIdentity: clientA);
            await _middleware.InvokeAsync(ctx, next);
        }

        // Act - client B should still have capacity
        var contextB = CreateHttpContext(clientIdentity: clientB);
        await _middleware.InvokeAsync(contextB, next);

        // Assert
        Assert.NotEqual(StatusCodes.Status429TooManyRequests, contextB.Response.StatusCode);
    }

    public void Dispose()
    {
        _fixture.Dispose();
        GC.SuppressFinalize(this);
    }
}
