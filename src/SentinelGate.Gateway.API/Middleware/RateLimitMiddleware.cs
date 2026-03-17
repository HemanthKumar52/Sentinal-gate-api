using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SentinelGate.Shared.Infrastructure.Data;
using SentinelGate.Shared.Infrastructure.Redis;
using SentinelGate.Shared.Models.Configuration;
using SentinelGate.Shared.Models.DTOs;
using SentinelGate.Shared.Models.Entities;
using SentinelGate.Shared.Models.Enums;

namespace SentinelGate.Gateway.API.Middleware;

/// <summary>
/// Core rate limiting middleware. Resolves the applicable policy for the request
/// (endpoint-specific > tenant-specific > global) and enforces rate limits.
/// Adds RFC rate limit headers to every response.
/// </summary>
public class RateLimitMiddleware : IMiddleware
{
    private readonly RateLimitCounter _counter;
    private readonly SentinelGateOptions _options;
    private readonly ILogger<RateLimitMiddleware> _logger;

    public RateLimitMiddleware(
        RateLimitCounter counter,
        IOptions<SentinelGateOptions> options,
        ILogger<RateLimitMiddleware> logger)
    {
        _counter = counter;
        _options = options.Value;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context, RequestDelegate next)
    {
        var clientIdentity = context.Items["ClientIdentity"]?.ToString() ?? "unknown";
        var tenantId = context.Items["TenantId"]?.ToString();
        var endpoint = context.Request.Path.Value ?? "/";

        // Resolve the applicable rate policy
        var dbContext = context.RequestServices.GetRequiredService<SentinelGateDbContext>();
        var policy = await ResolvePolicyAsync(dbContext, endpoint, tenantId);

        if (policy == null || !policy.IsEnabled)
        {
            // No active policy found, use defaults from options
            var defaultResult = await ApplyDefaultRateLimitAsync(clientIdentity);
            AddRateLimitHeaders(context, defaultResult, "default");
            if (!defaultResult.IsAllowed)
            {
                await WriteRateLimitResponse(context, defaultResult, "default");
                return;
            }

            await next(context);
            return;
        }

        // Apply rate limit based on policy algorithm
        var result = await ApplyPolicyRateLimitAsync(clientIdentity, policy);
        AddRateLimitHeaders(context, result, policy.Name);

        if (!result.IsAllowed)
        {
            context.Items["IsRateLimited"] = true;
            context.Items["RateLimitAlgorithm"] = result.Algorithm;

            _logger.LogWarning(
                "Rate limit exceeded for {ClientIdentity} on {Endpoint} (policy: {PolicyName})",
                clientIdentity, endpoint, policy.Name);

            await WriteRateLimitResponse(context, result, policy.Name);
            return;
        }

        await next(context);
    }

    private async Task<RatePolicy?> ResolvePolicyAsync(
        SentinelGateDbContext dbContext, string endpoint, string? tenantId)
    {
        var policies = await dbContext.RatePolicies
            .AsNoTracking()
            .Where(p => p.IsEnabled)
            .OrderByDescending(p => p.Priority)
            .ToListAsync();

        // 1. Endpoint-specific policy (highest priority)
        var endpointPolicy = policies.FirstOrDefault(p =>
            !string.IsNullOrEmpty(p.EndpointPattern) && MatchesEndpoint(endpoint, p.EndpointPattern));
        if (endpointPolicy != null)
            return endpointPolicy;

        // 2. Tenant-specific policy
        if (!string.IsNullOrEmpty(tenantId))
        {
            var tenantPolicy = policies.FirstOrDefault(p =>
                p.TenantId == tenantId && string.IsNullOrEmpty(p.EndpointPattern));
            if (tenantPolicy != null)
                return tenantPolicy;
        }

        // 3. Global policy
        return policies.FirstOrDefault(p => p.IsGlobal);
    }

    private static bool MatchesEndpoint(string endpoint, string pattern)
    {
        // Support simple wildcard matching: /api/users/* matches /api/users/123
        if (pattern.EndsWith("*"))
        {
            var prefix = pattern[..^1];
            return endpoint.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
        }

        return string.Equals(endpoint, pattern, StringComparison.OrdinalIgnoreCase);
    }

    private async Task<RateLimitResult> ApplyPolicyRateLimitAsync(string clientKey, RatePolicy policy)
    {
        return policy.Algorithm switch
        {
            RateLimitAlgorithm.FixedWindow =>
                await _counter.IncrementFixedWindow(clientKey, policy.WindowSeconds, policy.Limit),
            RateLimitAlgorithm.SlidingWindow =>
                await _counter.IncrementSlidingWindow(clientKey, policy.WindowSeconds, policy.Limit),
            RateLimitAlgorithm.TokenBucket =>
                await _counter.IncrementTokenBucket(clientKey, policy.BurstLimit ?? policy.Limit, policy.RefillRate ?? 10.0),
            RateLimitAlgorithm.LeakyBucket =>
                await _counter.IncrementLeakyBucket(clientKey, policy.LeakyCapacity ?? policy.Limit, policy.LeakyRate ?? 10.0),
            _ => await _counter.IncrementSlidingWindow(clientKey, policy.WindowSeconds, policy.Limit)
        };
    }

    private async Task<RateLimitResult> ApplyDefaultRateLimitAsync(string clientKey)
    {
        var defaults = _options.RateLimiting;
        return defaults.DefaultAlgorithm switch
        {
            RateLimitAlgorithm.FixedWindow =>
                await _counter.IncrementFixedWindow(clientKey, defaults.DefaultWindowSeconds, defaults.DefaultLimit),
            RateLimitAlgorithm.TokenBucket =>
                await _counter.IncrementTokenBucket(clientKey, defaults.DefaultBurstLimit, defaults.DefaultRefillRate),
            _ => await _counter.IncrementSlidingWindow(clientKey, defaults.DefaultWindowSeconds, defaults.DefaultLimit)
        };
    }

    private static void AddRateLimitHeaders(HttpContext context, RateLimitResult result, string policyName)
    {
        context.Response.Headers["X-RateLimit-Limit"] = result.Limit.ToString();
        context.Response.Headers["X-RateLimit-Remaining"] = result.Remaining.ToString();
        context.Response.Headers["X-RateLimit-Reset"] = new DateTimeOffset(result.ResetAt).ToUnixTimeSeconds().ToString();
        context.Response.Headers["X-RateLimit-Policy"] = policyName;

        if (!result.IsAllowed && result.RetryAfter.HasValue)
        {
            context.Response.Headers["Retry-After"] = ((int)Math.Ceiling(result.RetryAfter.Value.TotalSeconds)).ToString();
        }
    }

    private static async Task WriteRateLimitResponse(HttpContext context, RateLimitResult result, string policyName)
    {
        context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsJsonAsync(new
        {
            error = "Too Many Requests",
            message = "Rate limit exceeded. Please retry after the specified time.",
            retryAfterSeconds = result.RetryAfter.HasValue
                ? (int)Math.Ceiling(result.RetryAfter.Value.TotalSeconds)
                : 0,
            limit = result.Limit,
            remaining = result.Remaining,
            resetAt = result.ResetAt,
            policy = policyName
        });
    }
}
