using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SentinelGate.Shared.Infrastructure.Data;

namespace SentinelGate.Analytics.Service.Services;

public class DataRetentionService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<DataRetentionService> _logger;

    private const int BatchSize = 1000;
    private static readonly TimeSpan RawLogRetention = TimeSpan.FromDays(7);
    private static readonly TimeSpan HourlyRetention = TimeSpan.FromDays(90);
    private static readonly TimeSpan DailyRetention = TimeSpan.FromDays(730); // ~2 years

    public DataRetentionService(
        IServiceScopeFactory scopeFactory,
        ILogger<DataRetentionService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("DataRetentionService started");

        // Wait before first run
        await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await EnforceRetentionPolicies(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error enforcing retention policies");
            }

            // Run once per day
            await Task.Delay(TimeSpan.FromHours(24), stoppingToken);
        }

        _logger.LogInformation("DataRetentionService stopped");
    }

    private async Task EnforceRetentionPolicies(CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;

        // Delete raw RequestLogs older than 7 days
        var rawCutoff = now - RawLogRetention;
        var rawDeleted = await DeleteInBatches(
            async (db) =>
            {
                var ids = await db.RequestLogs
                    .Where(r => r.Timestamp < rawCutoff)
                    .OrderBy(r => r.Timestamp)
                    .Take(BatchSize)
                    .Select(r => r.Id)
                    .ToListAsync(cancellationToken);

                if (ids.Count == 0) return 0;

                return await db.RequestLogs
                    .Where(r => ids.Contains(r.Id))
                    .ExecuteDeleteAsync(cancellationToken);
            },
            cancellationToken);

        if (rawDeleted > 0)
            _logger.LogInformation("Deleted {Count} raw RequestLogs older than {Days} days", rawDeleted, RawLogRetention.Days);

        // Delete HourlyAggregates older than 90 days
        var hourlyCutoff = now - HourlyRetention;
        var hourlyDeleted = await DeleteInBatches(
            async (db) =>
            {
                var ids = await db.HourlyAggregates
                    .Where(h => h.Hour < hourlyCutoff)
                    .OrderBy(h => h.Hour)
                    .Take(BatchSize)
                    .Select(h => h.Id)
                    .ToListAsync(cancellationToken);

                if (ids.Count == 0) return 0;

                return await db.HourlyAggregates
                    .Where(h => ids.Contains(h.Id))
                    .ExecuteDeleteAsync(cancellationToken);
            },
            cancellationToken);

        if (hourlyDeleted > 0)
            _logger.LogInformation("Deleted {Count} HourlyAggregates older than {Days} days", hourlyDeleted, HourlyRetention.Days);

        // Delete DailyAggregates older than 2 years
        var dailyCutoff = DateOnly.FromDateTime(now - DailyRetention);
        var dailyDeleted = await DeleteInBatches(
            async (db) =>
            {
                var ids = await db.DailyAggregates
                    .Where(d => d.Date < dailyCutoff)
                    .OrderBy(d => d.Date)
                    .Take(BatchSize)
                    .Select(d => d.Id)
                    .ToListAsync(cancellationToken);

                if (ids.Count == 0) return 0;

                return await db.DailyAggregates
                    .Where(d => ids.Contains(d.Id))
                    .ExecuteDeleteAsync(cancellationToken);
            },
            cancellationToken);

        if (dailyDeleted > 0)
            _logger.LogInformation("Deleted {Count} DailyAggregates older than {Days} days", dailyDeleted, DailyRetention.Days);
    }

    private async Task<long> DeleteInBatches(
        Func<SentinelGateDbContext, Task<int>> deleteBatch,
        CancellationToken cancellationToken)
    {
        long totalDeleted = 0;

        while (!cancellationToken.IsCancellationRequested)
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<SentinelGateDbContext>();

            var deleted = await deleteBatch(db);
            totalDeleted += deleted;

            if (deleted < BatchSize)
                break;

            // Small pause between batches to reduce lock contention
            await Task.Delay(TimeSpan.FromMilliseconds(100), cancellationToken);
        }

        return totalDeleted;
    }
}
