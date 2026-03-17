using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SentinelGate.Analytics.Service.Services;
using SentinelGate.Shared.Infrastructure.Data;

namespace SentinelGate.Analytics.Service.Controllers;

[ApiController]
[Route("api/analytics")]
public class AnalyticsController : ControllerBase
{
    private readonly AggregationService _aggregationService;
    private readonly SentinelGateDbContext _db;

    public AnalyticsController(AggregationService aggregationService, SentinelGateDbContext db)
    {
        _aggregationService = aggregationService;
        _db = db;
    }

    [HttpGet("summary")]
    public async Task<IActionResult> GetSummary(
        [FromQuery] DateTime from,
        [FromQuery] DateTime to)
    {
        var result = await _aggregationService.GetTrafficSummary(from, to);
        return Ok(result);
    }

    [HttpGet("endpoints")]
    public async Task<IActionResult> GetEndpoints(
        [FromQuery] DateTime from,
        [FromQuery] DateTime to)
    {
        var result = await _aggregationService.GetEndpointStats(from, to);
        return Ok(result);
    }

    [HttpGet("clients/top")]
    public async Task<IActionResult> GetTopClients(
        [FromQuery] DateTime from,
        [FromQuery] DateTime to,
        [FromQuery] int top = 10)
    {
        var result = await _aggregationService.GetTopClients(from, to, top);
        return Ok(result);
    }

    [HttpGet("latency/percentiles")]
    public async Task<IActionResult> GetLatencyPercentiles(
        [FromQuery] DateTime from,
        [FromQuery] DateTime to)
    {
        var result = await _aggregationService.GetLatencyPercentiles(from, to);
        return Ok(result);
    }

    [HttpGet("reports/export")]
    public async Task<IActionResult> ExportRawLogs(
        [FromQuery] DateTime from,
        [FromQuery] DateTime to)
    {
        var logs = await _db.RequestLogs
            .Where(r => r.Timestamp >= from && r.Timestamp <= to)
            .OrderBy(r => r.Timestamp)
            .ToListAsync();

        var sb = new StringBuilder();
        sb.AppendLine("Id,ClientIdentity,ClientIp,ApiKey,TenantId,EndpointPath,HttpMethod,ResponseStatusCode,LatencyMs,RequestBodySize,ResponseSize,GeoCountry,UserAgent,IsBlocked,IsRateLimited,Timestamp");

        foreach (var log in logs)
        {
            sb.AppendLine(string.Join(",",
                log.Id,
                CsvEscape(log.ClientIdentity),
                CsvEscape(log.ClientIp),
                CsvEscape(log.ApiKey),
                CsvEscape(log.TenantId),
                CsvEscape(log.EndpointPath),
                log.HttpMethod,
                log.ResponseStatusCode,
                log.LatencyMs,
                log.RequestBodySize,
                log.ResponseSize,
                CsvEscape(log.GeoCountry),
                CsvEscape(log.UserAgent),
                log.IsBlocked,
                log.IsRateLimited,
                log.Timestamp.ToString("O")));
        }

        var bytes = Encoding.UTF8.GetBytes(sb.ToString());
        return File(bytes, "text/csv", $"request-logs-{from:yyyyMMdd}-{to:yyyyMMdd}.csv");
    }

    [HttpGet("health")]
    public IActionResult Health()
    {
        return Ok(new
        {
            Status = "Healthy",
            Service = "SentinelGate.Analytics.Service",
            Timestamp = DateTime.UtcNow
        });
    }

    private static string CsvEscape(string? value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        if (value.Contains(',') || value.Contains('"') || value.Contains('\n'))
            return $"\"{value.Replace("\"", "\"\"")}\"";
        return value;
    }
}
