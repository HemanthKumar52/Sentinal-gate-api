using Microsoft.AspNetCore.Mvc;
using SentinelGate.Dashboard.API.Services;

namespace SentinelGate.Dashboard.API.Controllers;

[ApiController]
[Route("api/dashboard")]
public class DashboardController : ControllerBase
{
    private readonly DashboardDataService _dataService;

    public DashboardController(DashboardDataService dataService)
    {
        _dataService = dataService;
    }

    [HttpGet("metrics")]
    public async Task<IActionResult> GetMetrics()
    {
        var metrics = await _dataService.GetLiveMetrics();
        return Ok(metrics);
    }

    [HttpGet("top-clients")]
    public async Task<IActionResult> GetTopClients([FromQuery] int hours = 1)
    {
        var clients = await _dataService.GetTopClients(hours);
        return Ok(clients);
    }

    [HttpGet("error-heatmap")]
    public async Task<IActionResult> GetErrorHeatmap([FromQuery] int days = 7)
    {
        var heatmap = await _dataService.GetErrorHeatmap(days);
        return Ok(heatmap);
    }

    [HttpGet("threat-leaderboard")]
    public async Task<IActionResult> GetThreatLeaderboard()
    {
        var leaderboard = await _dataService.GetThreatLeaderboard();
        return Ok(leaderboard);
    }

    [HttpGet("system-health")]
    public async Task<IActionResult> GetSystemHealth()
    {
        var health = await _dataService.GetSystemHealth();
        return Ok(health);
    }

    [HttpGet("health")]
    public IActionResult HealthCheck()
    {
        return Ok(new { Status = "Healthy", Service = "SentinelGate.Dashboard.API", Timestamp = DateTime.UtcNow });
    }
}
