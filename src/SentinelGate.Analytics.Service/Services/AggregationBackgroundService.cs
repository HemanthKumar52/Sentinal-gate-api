using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace SentinelGate.Analytics.Service.Services;

public class AggregationBackgroundService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<AggregationBackgroundService> _logger;

    public AggregationBackgroundService(
        IServiceScopeFactory scopeFactory,
        ILogger<AggregationBackgroundService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("AggregationBackgroundService started");

        // Wait a bit before first run to let the app start up
        await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var now = DateTime.UtcNow;

                // Always compute the previous hour's aggregates
                var previousHour = now.AddHours(-1);
                await RunHourlyAggregation(previousHour, stoppingToken);

                // If it's the first hour of the day (midnight), compute yesterday's daily aggregates
                if (now.Hour == 0)
                {
                    var yesterday = DateOnly.FromDateTime(now.AddDays(-1));
                    await RunDailyAggregation(yesterday, stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in aggregation background service");
            }

            // Wait until the next hour boundary
            var nextHour = DateTime.UtcNow
                .AddHours(1)
                .Date
                .AddHours(DateTime.UtcNow.AddHours(1).Hour);

            var delay = nextHour - DateTime.UtcNow;
            if (delay > TimeSpan.Zero)
            {
                _logger.LogDebug("Next aggregation run in {Delay}", delay);
                await Task.Delay(delay, stoppingToken);
            }
        }

        _logger.LogInformation("AggregationBackgroundService stopped");
    }

    private async Task RunHourlyAggregation(DateTime hour, CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<AggregationService>();

        _logger.LogInformation("Computing hourly aggregates for {Hour}", hour);
        await service.GenerateHourlyAggregates(hour);
    }

    private async Task RunDailyAggregation(DateOnly date, CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<AggregationService>();

        _logger.LogInformation("Computing daily aggregates for {Date}", date);
        await service.GenerateDailyAggregates(date);
    }
}
