using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SentinelGate.Shared.Infrastructure.Data;
using SentinelGate.Shared.Models.DTOs;
using SentinelGate.Shared.Models.Entities;

namespace SentinelGate.Analytics.Service.Services;

public class AggregationService
{
    private readonly SentinelGateDbContext _db;
    private readonly ILogger<AggregationService> _logger;

    public AggregationService(SentinelGateDbContext db, ILogger<AggregationService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<TrafficSummaryDto> GetTrafficSummary(DateTime from, DateTime to)
    {
        var logs = _db.RequestLogs
            .Where(r => r.Timestamp >= from && r.Timestamp <= to);

        var totalRequests = await logs.LongCountAsync();
        var blockedRequests = await logs.LongCountAsync(r => r.IsBlocked);
        var rateLimitedRequests = await logs.LongCountAsync(r => r.IsRateLimited);
        var avgLatency = totalRequests > 0
            ? await logs.AverageAsync(r => r.LatencyMs)
            : 0;
        var errorCount = await logs.LongCountAsync(r => r.ResponseStatusCode >= 400);
        var errorRate = totalRequests > 0 ? (double)errorCount / totalRequests * 100 : 0;

        var topEndpoints = await logs
            .GroupBy(r => r.EndpointPath)
            .Select(g => new EndpointStatDto(
                g.Key,
                g.LongCount(),
                g.LongCount(r => r.ResponseStatusCode >= 400),
                g.Average(r => r.LatencyMs)))
            .OrderByDescending(e => e.RequestCount)
            .Take(10)
            .ToListAsync();

        return new TrafficSummaryDto(
            totalRequests,
            blockedRequests,
            rateLimitedRequests,
            Math.Round(avgLatency, 2),
            Math.Round(errorRate, 2),
            topEndpoints,
            $"{from:yyyy-MM-dd HH:mm} - {to:yyyy-MM-dd HH:mm}");
    }

    public async Task<List<EndpointStatDto>> GetEndpointStats(DateTime from, DateTime to)
    {
        return await _db.RequestLogs
            .Where(r => r.Timestamp >= from && r.Timestamp <= to)
            .GroupBy(r => r.EndpointPath)
            .Select(g => new EndpointStatDto(
                g.Key,
                g.LongCount(),
                g.LongCount(r => r.ResponseStatusCode >= 400),
                g.Average(r => r.LatencyMs)))
            .OrderByDescending(e => e.RequestCount)
            .ToListAsync();
    }

    public async Task<List<ClientUsageDto>> GetTopClients(DateTime from, DateTime to, int top = 10)
    {
        return await _db.RequestLogs
            .Where(r => r.Timestamp >= from && r.Timestamp <= to && r.ClientIdentity != null)
            .GroupBy(r => r.ClientIdentity!)
            .Select(g => new ClientUsageDto(
                g.Key,
                g.LongCount(),
                g.LongCount(),
                0,
                g.LongCount(r => r.ResponseStatusCode >= 400) * 100.0 / g.LongCount()))
            .OrderByDescending(c => c.TotalRequests)
            .Take(top)
            .ToListAsync();
    }

    public async Task<object> GetLatencyPercentiles(DateTime from, DateTime to)
    {
        var latencies = await _db.RequestLogs
            .Where(r => r.Timestamp >= from && r.Timestamp <= to)
            .Select(r => r.LatencyMs)
            .OrderBy(l => l)
            .ToListAsync();

        if (latencies.Count == 0)
        {
            return new { P50 = 0.0, P95 = 0.0, P99 = 0.0 };
        }

        return new
        {
            P50 = GetPercentile(latencies, 50),
            P95 = GetPercentile(latencies, 95),
            P99 = GetPercentile(latencies, 99)
        };
    }

    public async Task GenerateHourlyAggregates(DateTime hour)
    {
        var hourStart = new DateTime(hour.Year, hour.Month, hour.Day, hour.Hour, 0, 0, DateTimeKind.Utc);
        var hourEnd = hourStart.AddHours(1);

        var aggregates = await _db.RequestLogs
            .Where(r => r.Timestamp >= hourStart && r.Timestamp < hourEnd)
            .GroupBy(r => r.EndpointPath)
            .Select(g => new
            {
                EndpointPath = g.Key,
                RequestCount = g.LongCount(),
                ErrorCount = g.LongCount(r => r.ResponseStatusCode >= 400),
                AvgLatencyMs = g.Average(r => r.LatencyMs),
                TotalRequestSize = g.Sum(r => r.RequestBodySize),
                TotalResponseSize = g.Sum(r => r.ResponseSize),
                Latencies = g.Select(r => r.LatencyMs).ToList()
            })
            .ToListAsync();

        foreach (var agg in aggregates)
        {
            var sorted = agg.Latencies.OrderBy(l => l).ToList();

            var existing = await _db.HourlyAggregates
                .FirstOrDefaultAsync(h => h.EndpointPath == agg.EndpointPath && h.Hour == hourStart);

            if (existing != null)
            {
                existing.RequestCount = agg.RequestCount;
                existing.ErrorCount = agg.ErrorCount;
                existing.AvgLatencyMs = Math.Round(agg.AvgLatencyMs, 2);
                existing.P50LatencyMs = GetPercentile(sorted, 50);
                existing.P95LatencyMs = GetPercentile(sorted, 95);
                existing.P99LatencyMs = GetPercentile(sorted, 99);
                existing.TotalRequestSize = agg.TotalRequestSize;
                existing.TotalResponseSize = agg.TotalResponseSize;
            }
            else
            {
                _db.HourlyAggregates.Add(new HourlyAggregate
                {
                    Id = Guid.NewGuid(),
                    EndpointPath = agg.EndpointPath,
                    Hour = hourStart,
                    RequestCount = agg.RequestCount,
                    ErrorCount = agg.ErrorCount,
                    AvgLatencyMs = Math.Round(agg.AvgLatencyMs, 2),
                    P50LatencyMs = GetPercentile(sorted, 50),
                    P95LatencyMs = GetPercentile(sorted, 95),
                    P99LatencyMs = GetPercentile(sorted, 99),
                    TotalRequestSize = agg.TotalRequestSize,
                    TotalResponseSize = agg.TotalResponseSize
                });
            }
        }

        await _db.SaveChangesAsync();
        _logger.LogInformation("Generated hourly aggregates for {Hour}: {Count} endpoints", hourStart, aggregates.Count);
    }

    public async Task GenerateDailyAggregates(DateOnly date)
    {
        var dayStart = date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var dayEnd = dayStart.AddDays(1);

        var aggregates = await _db.RequestLogs
            .Where(r => r.Timestamp >= dayStart && r.Timestamp < dayEnd)
            .GroupBy(r => r.EndpointPath)
            .Select(g => new
            {
                EndpointPath = g.Key,
                RequestCount = g.LongCount(),
                ErrorCount = g.LongCount(r => r.ResponseStatusCode >= 400),
                AvgLatencyMs = g.Average(r => r.LatencyMs),
                Latencies = g.Select(r => r.LatencyMs).ToList(),
                TopClient = g
                    .Where(r => r.ClientIdentity != null)
                    .GroupBy(r => r.ClientIdentity!)
                    .OrderByDescending(cg => cg.Count())
                    .Select(cg => new { Identity = cg.Key, Count = (long)cg.Count() })
                    .FirstOrDefault()
            })
            .ToListAsync();

        foreach (var agg in aggregates)
        {
            var sorted = agg.Latencies.OrderBy(l => l).ToList();

            var existing = await _db.DailyAggregates
                .FirstOrDefaultAsync(d => d.EndpointPath == agg.EndpointPath && d.Date == date);

            if (existing != null)
            {
                existing.RequestCount = agg.RequestCount;
                existing.ErrorCount = agg.ErrorCount;
                existing.AvgLatencyMs = Math.Round(agg.AvgLatencyMs, 2);
                existing.P50LatencyMs = GetPercentile(sorted, 50);
                existing.P95LatencyMs = GetPercentile(sorted, 95);
                existing.P99LatencyMs = GetPercentile(sorted, 99);
                existing.TopClientIdentity = agg.TopClient?.Identity;
                existing.TopClientRequests = agg.TopClient?.Count ?? 0;
            }
            else
            {
                _db.DailyAggregates.Add(new DailyAggregate
                {
                    Id = Guid.NewGuid(),
                    EndpointPath = agg.EndpointPath,
                    Date = date,
                    RequestCount = agg.RequestCount,
                    ErrorCount = agg.ErrorCount,
                    AvgLatencyMs = Math.Round(agg.AvgLatencyMs, 2),
                    P50LatencyMs = GetPercentile(sorted, 50),
                    P95LatencyMs = GetPercentile(sorted, 95),
                    P99LatencyMs = GetPercentile(sorted, 99),
                    TopClientIdentity = agg.TopClient?.Identity,
                    TopClientRequests = agg.TopClient?.Count ?? 0
                });
            }
        }

        await _db.SaveChangesAsync();
        _logger.LogInformation("Generated daily aggregates for {Date}: {Count} endpoints", date, aggregates.Count);
    }

    private static double GetPercentile(List<double> sortedValues, int percentile)
    {
        if (sortedValues.Count == 0) return 0;

        var index = (percentile / 100.0) * (sortedValues.Count - 1);
        var lower = (int)Math.Floor(index);
        var upper = (int)Math.Ceiling(index);

        if (lower == upper)
            return Math.Round(sortedValues[lower], 2);

        var weight = index - lower;
        return Math.Round(sortedValues[lower] * (1 - weight) + sortedValues[upper] * weight, 2);
    }
}
