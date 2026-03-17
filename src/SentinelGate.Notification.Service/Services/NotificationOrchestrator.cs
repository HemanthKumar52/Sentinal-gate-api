using Microsoft.EntityFrameworkCore;
using SentinelGate.Shared.Infrastructure.Data;
using SentinelGate.Shared.Models.Entities;

namespace SentinelGate.Notification.Service.Services;

public class NotificationOrchestrator
{
    private readonly SentinelGateDbContext _db;
    private readonly WebhookDispatcher _webhookDispatcher;
    private readonly SlackNotifier _slackNotifier;
    private readonly TeamsNotifier _teamsNotifier;
    private readonly EmailNotifier _emailNotifier;
    private readonly IConfiguration _configuration;
    private readonly ILogger<NotificationOrchestrator> _logger;

    public NotificationOrchestrator(
        SentinelGateDbContext db,
        WebhookDispatcher webhookDispatcher,
        SlackNotifier slackNotifier,
        TeamsNotifier teamsNotifier,
        EmailNotifier emailNotifier,
        IConfiguration configuration,
        ILogger<NotificationOrchestrator> logger)
    {
        _db = db;
        _webhookDispatcher = webhookDispatcher;
        _slackNotifier = slackNotifier;
        _teamsNotifier = teamsNotifier;
        _emailNotifier = emailNotifier;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task ProcessEvent(AlertEvent alertEvent)
    {
        // Persist the alert event
        alertEvent.Id = alertEvent.Id == Guid.Empty ? Guid.NewGuid() : alertEvent.Id;
        alertEvent.CreatedAt = DateTime.UtcNow;
        _db.AlertEvents.Add(alertEvent);
        await _db.SaveChangesAsync();

        _logger.LogInformation(
            "Processing alert event {EventId} of type {EventType} with severity {Severity}",
            alertEvent.Id, alertEvent.EventType, alertEvent.Severity);

        var message = $"[{alertEvent.Severity}] {alertEvent.EventType}: {alertEvent.Details}";

        // Dispatch to all active webhooks that subscribe to this event type
        var webhooks = await _db.WebhookSubscriptions
            .Where(w => w.IsActive)
            .ToListAsync();

        var matchingWebhooks = webhooks
            .Where(w => string.IsNullOrEmpty(w.Events) ||
                        w.Events.Split(',', StringSplitOptions.RemoveEmptyEntries)
                            .Any(e => e.Trim().Equals(alertEvent.EventType, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        foreach (var webhook in matchingWebhooks)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    await _webhookDispatcher.DispatchAsync(webhook, new
                    {
                        alertEvent.Id,
                        alertEvent.EventType,
                        alertEvent.Severity,
                        alertEvent.Details,
                        alertEvent.ClientIdentity,
                        alertEvent.CreatedAt
                    });
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to dispatch webhook to {Url}", webhook.Url);
                }
            });
        }

        // Send Slack notification if configured
        var slackUrl = _configuration["Notification:Slack:WebhookUrl"];
        if (!string.IsNullOrWhiteSpace(slackUrl))
        {
            var slackChannel = _configuration["Notification:Slack:Channel"];
            _ = Task.Run(async () =>
            {
                try
                {
                    await _slackNotifier.SendAsync(slackUrl, message, slackChannel);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to send Slack notification");
                }
            });
        }

        // Send Teams notification if configured
        var teamsUrl = _configuration["Notification:Teams:WebhookUrl"];
        if (!string.IsNullOrWhiteSpace(teamsUrl))
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    await _teamsNotifier.SendAsync(teamsUrl, message);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to send Teams notification");
                }
            });
        }

        // Send email notification if configured
        var emailTo = _configuration["Notification:Email:AlertRecipient"];
        if (!string.IsNullOrWhiteSpace(emailTo))
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    await _emailNotifier.SendAsync(
                        emailTo,
                        $"SentinelGate Alert: {alertEvent.EventType}",
                        $"<h2>{alertEvent.EventType}</h2><p><strong>Severity:</strong> {alertEvent.Severity}</p><p>{alertEvent.Details}</p><p><em>{alertEvent.CreatedAt:u}</em></p>");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to send email notification");
                }
            });
        }
    }
}
