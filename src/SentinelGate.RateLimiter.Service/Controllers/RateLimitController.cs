using Microsoft.AspNetCore.Mvc;
using SentinelGate.RateLimiter.Service.Services;
using SentinelGate.Shared.Infrastructure.Redis;
using SentinelGate.Shared.Models.DTOs;
using SentinelGate.Shared.Models.Enums;

namespace SentinelGate.RateLimiter.Service.Controllers;

[ApiController]
[Route("api/ratelimit")]
public class RateLimitController : ControllerBase
{
    private readonly RateLimiterFactory _factory;
    private readonly RedisConnectionManager _redis;
    private readonly ILogger<RateLimitController> _logger;

    public RateLimitController(
        RateLimiterFactory factory,
        RedisConnectionManager redis,
        ILogger<RateLimitController> logger)
    {
        _factory = factory;
        _redis = redis;
        _logger = logger;
    }

    /// <summary>
    /// Check rate limit for a client request.
    /// </summary>
    [HttpPost("check")]
    [ProducesResponseType(typeof(RateLimitResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> Check([FromBody] RateLimitCheckRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.ClientIdentity))
            return BadRequest(new { error = "ClientIdentity is required." });

        var clientKey = string.IsNullOrWhiteSpace(request.EndpointPath)
            ? request.ClientIdentity
            : $"{request.ClientIdentity}:{request.EndpointPath}";

        try
        {
            var result = await _factory.CheckLimit(
                request.Algorithm,
                clientKey,
                request.Limit,
                request.WindowSeconds,
                request.BurstLimit,
                request.RefillRate
            );

            if (!result.IsAllowed)
            {
                Response.Headers["Retry-After"] = result.RetryAfter?.TotalSeconds.ToString("F0") ?? "60";
                Response.Headers["X-RateLimit-Limit"] = result.Limit.ToString();
                Response.Headers["X-RateLimit-Remaining"] = result.Remaining.ToString();
                Response.Headers["X-RateLimit-Reset"] = new DateTimeOffset(result.ResetAt).ToUnixTimeSeconds().ToString();
                return StatusCode(StatusCodes.Status429TooManyRequests, result);
            }

            Response.Headers["X-RateLimit-Limit"] = result.Limit.ToString();
            Response.Headers["X-RateLimit-Remaining"] = result.Remaining.ToString();
            Response.Headers["X-RateLimit-Reset"] = new DateTimeOffset(result.ResetAt).ToUnixTimeSeconds().ToString();
            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking rate limit for {ClientKey}", clientKey);
            return StatusCode(StatusCodes.Status500InternalServerError,
                new { error = "Rate limit check failed.", detail = ex.Message });
        }
    }

    /// <summary>
    /// Get current counter state for a client across all algorithms.
    /// </summary>
    [HttpGet("counters/{clientIdentity}")]
    [ProducesResponseType(typeof(ClientCounterState), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetCounters(string clientIdentity)
    {
        var db = _redis.GetDatabase();
        var state = new ClientCounterState
        {
            ClientIdentity = clientIdentity,
            RedisConnected = db != null,
            Counters = new List<CounterEntry>()
        };

        if (db != null)
        {
            try
            {
                var server = db.Multiplexer.GetServer(db.Multiplexer.GetEndPoints().First());
                var patterns = new[] { $"rl:fw:{clientIdentity}:*", $"rl:sw:{clientIdentity}", $"rl:tb:{clientIdentity}", $"rl:lb:{clientIdentity}" };

                foreach (var pattern in patterns)
                {
                    await foreach (var key in server.KeysAsync(pattern: pattern))
                    {
                        var keyType = await db.KeyTypeAsync(key);
                        string? value = null;
                        long ttl = -1;

                        switch (keyType)
                        {
                            case StackExchange.Redis.RedisType.String:
                                value = await db.StringGetAsync(key);
                                break;
                            case StackExchange.Redis.RedisType.SortedSet:
                                var count = await db.SortedSetLengthAsync(key);
                                value = $"entries: {count}";
                                break;
                            case StackExchange.Redis.RedisType.Hash:
                                var entries = await db.HashGetAllAsync(key);
                                value = string.Join(", ", entries.Select(e => $"{e.Name}={e.Value}"));
                                break;
                        }

                        var keyTtl = await db.KeyTimeToLiveAsync(key);
                        ttl = keyTtl.HasValue ? (long)keyTtl.Value.TotalSeconds : -1;

                        state.Counters.Add(new CounterEntry
                        {
                            Key = key.ToString(),
                            Type = keyType.ToString(),
                            Value = value ?? "N/A",
                            TtlSeconds = ttl
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to retrieve counters for {ClientIdentity}", clientIdentity);
                state.Error = ex.Message;
            }
        }
        else
        {
            state.Error = "Redis unavailable; in-memory fallback does not expose counter state.";
        }

        return Ok(state);
    }

    /// <summary>
    /// Reset all rate limit counters for a client.
    /// </summary>
    [HttpDelete("counters/{clientIdentity}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> ResetCounters(string clientIdentity)
    {
        var db = _redis.GetDatabase();
        if (db == null)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable,
                new { error = "Redis unavailable. Cannot reset counters." });
        }

        try
        {
            var server = db.Multiplexer.GetServer(db.Multiplexer.GetEndPoints().First());
            var deletedCount = 0;
            var patterns = new[] { $"rl:fw:{clientIdentity}:*", $"rl:sw:{clientIdentity}", $"rl:tb:{clientIdentity}", $"rl:lb:{clientIdentity}" };

            foreach (var pattern in patterns)
            {
                await foreach (var key in server.KeysAsync(pattern: pattern))
                {
                    await db.KeyDeleteAsync(key);
                    deletedCount++;
                }
            }

            _logger.LogInformation("Reset {Count} counters for client {ClientIdentity}", deletedCount, clientIdentity);
            return Ok(new { clientIdentity, deletedKeys = deletedCount });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error resetting counters for {ClientIdentity}", clientIdentity);
            return StatusCode(StatusCodes.Status500InternalServerError,
                new { error = "Failed to reset counters.", detail = ex.Message });
        }
    }

    /// <summary>
    /// Health check endpoint.
    /// </summary>
    [HttpGet("health")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public IActionResult Health()
    {
        var redisConnected = _redis.IsConnected;
        return Ok(new
        {
            status = "healthy",
            service = "SentinelGate.RateLimiter.Service",
            timestamp = DateTime.UtcNow,
            redis = redisConnected ? "connected" : "disconnected (using in-memory fallback)"
        });
    }
}

// ─── Request / Response DTOs ─────────────────────────────────────────────────

public record RateLimitCheckRequest
{
    public string ClientIdentity { get; init; } = string.Empty;
    public string? EndpointPath { get; init; }
    public RateLimitAlgorithm Algorithm { get; init; } = RateLimitAlgorithm.FixedWindow;
    public int Limit { get; init; } = 100;
    public int WindowSeconds { get; init; } = 60;
    public int? BurstLimit { get; init; }
    public double? RefillRate { get; init; }
}

public class ClientCounterState
{
    public string ClientIdentity { get; set; } = string.Empty;
    public bool RedisConnected { get; set; }
    public string? Error { get; set; }
    public List<CounterEntry> Counters { get; set; } = new();
}

public class CounterEntry
{
    public string Key { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public long TtlSeconds { get; set; }
}
