using Microsoft.EntityFrameworkCore;
using SentinelGate.Shared.Infrastructure.Data;
using SentinelGate.Shared.Models.DTOs;

namespace SentinelGate.Dashboard.API.Services;

public class DashboardDataService
{
    private readonly SentinelGateDbContext _db;
    private readonly ILogger<DashboardDataService> _logger;

    public DashboardDataService(SentinelGateDbContext db, ILogger<DashboardDataService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<DashboardMetricsDto> GetLiveMetrics()
    {
        var now = DateTime.UtcNow;
        var fiveSecondsAgo = now.AddSeconds(-5);
        var oneMinuteAgo = now.AddMinutes(-1);

        var recentLogs = await _db.RequestLogs
            .Where(r => r.Timestamp >= fiveSecondsAgo)
            .ToListAsync();

        var requestsPerSecond = recentLogs.Count / 5.0;

        var activeClients = await _db.RequestLogs
            .Where(r => r.Timestamp >= oneMinuteAgo)
            .Select(r => r.ClientIdentity)
            .Distinct()
            .CountAsync();

        var blockedClients = await _db.BlockedClients
            .Where(b => b.IsActive && !b.IsDeleted)
            .CountAsync();

        var avgLatency = recentLogs.Count > 0
            ? recentLogs.Average(r => r.LatencyMs)
            : 0.0;

        var errorRate = recentLogs.Count > 0
            ? recentLogs.Count(r => r.ResponseStatusCode >= 400) / (double)recentLogs.Count
            : 0.0;

        var topClients = await GetTopClients(1);
        var top5 = topClients.Take(5).ToList();

        return new DashboardMetricsDto(
            RequestsPerSecond: Math.Round(requestsPerSecond, 2),
            ActiveClients: activeClients,
            BlockedClients: blockedClients,
            AverageLatencyMs: Math.Round(avgLatency, 2),
            ErrorRate: Math.Round(errorRate, 4),
            TopClients: top5
        );
    }

    public async Task<List<ClientUsageDto>> GetTopClients(int hours = 1)
    {
        var since = DateTime.UtcNow.AddHours(-hours);

        var grouped = await _db.RequestLogs
            .Where(r => r.Timestamp >= since && r.ClientIdentity != null)
            .GroupBy(r => r.ClientIdentity!)
            .Select(g => new
            {
                ClientIdentity = g.Key,
                TotalRequests = g.LongCount(),
                ErrorCount = g.Count(r => r.ResponseStatusCode >= 400),
                TotalCount = g.Count()
            })
            .OrderByDescending(c => c.TotalRequests)
            .Take(20)
            .ToListAsync();

        var topClients = grouped.Select(g => new ClientUsageDto(
            g.ClientIdentity,
            g.TotalRequests,
            g.TotalRequests,
            0,
            g.TotalCount > 0 ? g.ErrorCount / (double)g.TotalCount : 0.0
        )).ToList();

        return topClients;
    }

    public async Task<Dictionary<string, Dictionary<int, int>>> GetErrorHeatmap(int days = 7)
    {
        var since = DateTime.UtcNow.AddDays(-days);

        var errors = await _db.RequestLogs
            .Where(r => r.Timestamp >= since && r.ResponseStatusCode >= 400)
            .Select(r => new { r.EndpointPath, r.Timestamp })
            .ToListAsync();

        var heatmap = new Dictionary<string, Dictionary<int, int>>();

        foreach (var error in errors)
        {
            if (!heatmap.ContainsKey(error.EndpointPath))
                heatmap[error.EndpointPath] = new Dictionary<int, int>();

            var hour = error.Timestamp.Hour;
            if (!heatmap[error.EndpointPath].ContainsKey(hour))
                heatmap[error.EndpointPath][hour] = 0;

            heatmap[error.EndpointPath][hour]++;
        }

        return heatmap;
    }

    public async Task<List<object>> GetThreatLeaderboard()
    {
        var threats = await _db.ThreatScores
            .OrderByDescending(t => t.Score)
            .Take(20)
            .Select(t => (object)new
            {
                t.ClientIdentity,
                t.IpAddress,
                t.Score,
                t.RateLimitViolations,
                t.High4xxRate,
                t.AuthFailures,
                t.SingleEndpointHammering,
                t.UserAgentAnomaly,
                t.GeoMismatch,
                t.PayloadAnomaly,
                t.LastUpdated
            })
            .ToListAsync();

        return threats;
    }

    public async Task<object> GetSystemHealth()
    {
        var now = DateTime.UtcNow;
        var oneMinuteAgo = now.AddMinutes(-1);

        var recentLogs = await _db.RequestLogs
            .Where(r => r.Timestamp >= oneMinuteAgo)
            .ToListAsync();

        var avgLatency = recentLogs.Count > 0
            ? recentLogs.Average(r => r.LatencyMs)
            : 0.0;

        var totalRequests = recentLogs.Count;
        var errorCount = recentLogs.Count(r => r.ResponseStatusCode >= 500);
        var rateLimitedCount = recentLogs.Count(r => r.IsRateLimited);
        var blockedCount = recentLogs.Count(r => r.IsBlocked);

        return new
        {
            MiddlewareLatencyMs = Math.Round(avgLatency, 2),
            CacheHitRate = 0.0,
            DbQueueDepth = 0,
            TotalRequestsLastMinute = totalRequests,
            ServerErrorsLastMinute = errorCount,
            RateLimitedLastMinute = rateLimitedCount,
            BlockedLastMinute = blockedCount,
            Timestamp = now
        };
    }
}
