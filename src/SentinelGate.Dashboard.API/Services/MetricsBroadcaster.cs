using Microsoft.AspNetCore.SignalR;
using SentinelGate.Dashboard.API.Hubs;

namespace SentinelGate.Dashboard.API.Services;

public class MetricsBroadcaster : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IHubContext<DashboardHub, IDashboardClient> _hubContext;
    private readonly ILogger<MetricsBroadcaster> _logger;

    public MetricsBroadcaster(
        IServiceProvider serviceProvider,
        IHubContext<DashboardHub, IDashboardClient> hubContext,
        ILogger<MetricsBroadcaster> logger)
    {
        _serviceProvider = serviceProvider;
        _hubContext = hubContext;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("MetricsBroadcaster started");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var dataService = scope.ServiceProvider.GetRequiredService<DashboardDataService>();

                var metrics = await dataService.GetLiveMetrics();

                await _hubContext.Clients.Group("MetricsSubscribers").ReceiveMetrics(metrics);
                await _hubContext.Clients.Group("Admins").ReceiveMetrics(metrics);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error broadcasting metrics");
            }

            await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
        }

        _logger.LogInformation("MetricsBroadcaster stopped");
    }
}
