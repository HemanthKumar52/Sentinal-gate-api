namespace SentinelGate.ThreatDetection.Service.Services;

/// <summary>
/// Background service that periodically decays all threat scores using exponential decay.
/// Runs every hour by default.
/// </summary>
public class ThreatDecayBackgroundService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<ThreatDecayBackgroundService> _logger;
    private readonly TimeSpan _interval = TimeSpan.FromHours(1);

    public ThreatDecayBackgroundService(
        IServiceProvider serviceProvider,
        ILogger<ThreatDecayBackgroundService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Threat decay background service started (interval: {Interval})", _interval);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(_interval, stoppingToken);

                using var scope = _serviceProvider.CreateScope();
                var scoringEngine = scope.ServiceProvider.GetRequiredService<ThreatScoringEngine>();
                await scoringEngine.DecayScores();
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // Graceful shutdown
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during threat score decay cycle");
            }
        }

        _logger.LogInformation("Threat decay background service stopped");
    }
}
