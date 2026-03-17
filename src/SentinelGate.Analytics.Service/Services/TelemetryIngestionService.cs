using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SentinelGate.Shared.Infrastructure.Data;
using SentinelGate.Shared.Infrastructure.Services;
using SentinelGate.Shared.Models.Entities;

namespace SentinelGate.Analytics.Service.Services;

public class TelemetryIngestionService : BackgroundService
{
    private readonly TelemetryChannel _channel;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<TelemetryIngestionService> _logger;

    private const int MaxBatchSize = 100;
    private static readonly TimeSpan FlushInterval = TimeSpan.FromSeconds(1);

    public TelemetryIngestionService(
        TelemetryChannel channel,
        IServiceScopeFactory scopeFactory,
        ILogger<TelemetryIngestionService> logger)
    {
        _channel = channel;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("TelemetryIngestionService started");

        var batch = new List<RequestLog>(MaxBatchSize);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                cts.CancelAfter(FlushInterval);

                try
                {
                    await foreach (var log in _channel.ReadAllAsync(cts.Token))
                    {
                        batch.Add(log);

                        if (batch.Count >= MaxBatchSize)
                        {
                            await FlushBatchAsync(batch, stoppingToken);
                        }
                    }
                }
                catch (OperationCanceledException) when (!stoppingToken.IsCancellationRequested)
                {
                    // Timer expired - flush whatever we have
                }

                if (batch.Count > 0)
                {
                    await FlushBatchAsync(batch, stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in telemetry ingestion loop");
                await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
            }
        }

        // Final flush on shutdown
        if (batch.Count > 0)
        {
            await FlushBatchAsync(batch, CancellationToken.None);
        }

        _logger.LogInformation("TelemetryIngestionService stopped");
    }

    private async Task FlushBatchAsync(List<RequestLog> batch, CancellationToken cancellationToken)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<SentinelGateDbContext>();

            db.RequestLogs.AddRange(batch);
            await db.SaveChangesAsync(cancellationToken);

            _logger.LogDebug("Flushed {Count} telemetry records to database", batch.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to flush {Count} telemetry records", batch.Count);
        }
        finally
        {
            batch.Clear();
        }
    }
}
