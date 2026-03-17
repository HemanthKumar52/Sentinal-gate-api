using Microsoft.EntityFrameworkCore;
using SentinelGate.Shared.Infrastructure.Data;
using SentinelGate.Shared.Infrastructure.Redis;

namespace SentinelGate.Gateway.API.Middleware;

/// <summary>
/// Checks if the client identity or IP is in the block list.
/// First checks Redis cache, then falls back to the database.
/// Returns 403 if the client is blocked.
/// </summary>
public class BlockListCheckMiddleware : IMiddleware
{
    private readonly RedisConnectionManager _redis;
    private readonly ILogger<BlockListCheckMiddleware> _logger;

    public BlockListCheckMiddleware(
        RedisConnectionManager redis,
        ILogger<BlockListCheckMiddleware> logger)
    {
        _redis = redis;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context, RequestDelegate next)
    {
        var clientIdentity = context.Items["ClientIdentity"]?.ToString() ?? "unknown";
        var clientIp = context.Items["ClientIp"]?.ToString() ?? "unknown";

        var (isBlocked, reason) = await CheckBlockedAsync(clientIdentity, clientIp, context);

        if (isBlocked)
        {
            _logger.LogWarning("Blocked request from {ClientIdentity} ({ClientIp}): {Reason}",
                clientIdentity, clientIp, reason);

            context.Items["IsBlocked"] = true;

            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsJsonAsync(new
            {
                error = "Forbidden",
                message = "Your access has been blocked",
                clientIdentity,
                reason
            });
            return;
        }

        await next(context);
    }

    private async Task<(bool IsBlocked, string? Reason)> CheckBlockedAsync(
        string clientIdentity, string clientIp, HttpContext context)
    {
        // 1. Check Redis cache first
        var db = _redis.GetDatabase();
        if (db != null)
        {
            try
            {
                var identityResult = await db.StringGetAsync($"blocked:{clientIdentity}");
                if (identityResult.HasValue)
                    return (true, identityResult.ToString());

                var ipResult = await db.StringGetAsync($"blocked:ip:{clientIp}");
                if (ipResult.HasValue)
                    return (true, ipResult.ToString());
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Redis check failed for block list, falling back to DB");
            }
        }

        // 2. Fall back to database
        var dbContext = context.RequestServices.GetRequiredService<SentinelGateDbContext>();
        var now = DateTime.UtcNow;

        var blockedEntry = await dbContext.BlockedClients
            .AsNoTracking()
            .Where(b => b.IsActive && !b.IsDeleted)
            .Where(b => (b.ExpiresAt == null || b.ExpiresAt > now))
            .Where(b => b.ClientIdentity == clientIdentity || b.IpAddress == clientIp)
            .FirstOrDefaultAsync();

        if (blockedEntry != null)
        {
            // Cache the block in Redis for faster subsequent checks
            if (db != null)
            {
                try
                {
                    var ttl = blockedEntry.ExpiresAt.HasValue
                        ? blockedEntry.ExpiresAt.Value - now
                        : TimeSpan.FromHours(1);

                    if (ttl > TimeSpan.Zero)
                    {
                        var cacheKey = blockedEntry.ClientIdentity == clientIdentity
                            ? $"blocked:{clientIdentity}"
                            : $"blocked:ip:{clientIp}";
                        await db.StringSetAsync(cacheKey, blockedEntry.Reason ?? "Blocked", ttl);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to cache block entry in Redis");
                }
            }

            return (true, blockedEntry.Reason);
        }

        return (false, null);
    }
}
