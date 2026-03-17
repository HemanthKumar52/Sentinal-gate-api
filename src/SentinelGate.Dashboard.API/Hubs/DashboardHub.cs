using Microsoft.AspNetCore.SignalR;
using SentinelGate.Shared.Models.DTOs;

namespace SentinelGate.Dashboard.API.Hubs;

public interface IDashboardClient
{
    Task ReceiveMetrics(DashboardMetricsDto metrics);
    Task ReceiveDecision(object decision);
    Task ReceiveAlert(object alert);
}

public class DashboardHub : Hub<IDashboardClient>
{
    private readonly ILogger<DashboardHub> _logger;

    public DashboardHub(ILogger<DashboardHub> logger)
    {
        _logger = logger;
    }

    public override async Task OnConnectedAsync()
    {
        _logger.LogInformation("Client connected: {ConnectionId}", Context.ConnectionId);
        await Groups.AddToGroupAsync(Context.ConnectionId, "Admins");
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        _logger.LogInformation("Client disconnected: {ConnectionId}, Reason: {Reason}",
            Context.ConnectionId, exception?.Message ?? "Normal closure");
        await base.OnDisconnectedAsync(exception);
    }

    public async Task SubscribeToMetrics()
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, "MetricsSubscribers");
        _logger.LogInformation("Client {ConnectionId} subscribed to metrics", Context.ConnectionId);
    }

    public async Task SubscribeToAlerts()
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, "AlertSubscribers");
        _logger.LogInformation("Client {ConnectionId} subscribed to alerts", Context.ConnectionId);
    }

    public async Task SubscribeToDecisions()
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, "DecisionSubscribers");
        _logger.LogInformation("Client {ConnectionId} subscribed to decisions", Context.ConnectionId);
    }
}
