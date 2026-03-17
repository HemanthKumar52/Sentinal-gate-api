using Microsoft.AspNetCore.Mvc;
using SentinelGate.Shared.Infrastructure.Data;
using SentinelGate.Shared.Infrastructure.Redis;

namespace SentinelGate.Gateway.API.Controllers;

/// <summary>
/// Health check endpoint for the SentinelGate Gateway API.
/// Reports service status and connectivity to Redis and the database.
/// </summary>
[ApiController]
[Route("health")]
[Produces("application/json")]
public class HealthController : ControllerBase
{
    private readonly SentinelGateDbContext _dbContext;
    private readonly RedisConnectionManager _redis;
    private readonly ILogger<HealthController> _logger;

    public HealthController(
        SentinelGateDbContext dbContext,
        RedisConnectionManager redis,
        ILogger<HealthController> logger)
    {
        _dbContext = dbContext;
        _redis = redis;
        _logger = logger;
    }

    /// <summary>
    /// Returns the health status of the Gateway API, including Redis and database connectivity.
    /// </summary>
    /// <returns>Health status object with service details</returns>
    [HttpGet]
    [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(object), StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> GetHealth()
    {
        var redisConnected = false;
        var databaseConnected = false;

        // Check Redis connectivity
        try
        {
            redisConnected = _redis.IsConnected;
            if (!redisConnected)
                redisConnected = _redis.TryConnect();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Redis health check failed");
        }

        // Check database connectivity
        try
        {
            databaseConnected = await _dbContext.Database.CanConnectAsync();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Database health check failed");
        }

        var isHealthy = databaseConnected; // Database is required, Redis is optional

        var result = new
        {
            status = isHealthy ? "healthy" : "degraded",
            service = "SentinelGate.Gateway",
            timestamp = DateTime.UtcNow,
            redis = redisConnected ? "connected" : "disconnected",
            database = databaseConnected ? "connected" : "disconnected"
        };

        return isHealthy
            ? Ok(result)
            : StatusCode(StatusCodes.Status503ServiceUnavailable, result);
    }
}
