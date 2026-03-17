using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SentinelGate.Notification.Service.Services;
using SentinelGate.Shared.Infrastructure.Data;
using SentinelGate.Shared.Models.DTOs;
using SentinelGate.Shared.Models.Entities;
using SentinelGate.Shared.Models.Enums;

namespace SentinelGate.Notification.Service.Controllers;

[ApiController]
[Route("api/notifications")]
public class NotificationController : ControllerBase
{
    private readonly SentinelGateDbContext _db;
    private readonly NotificationOrchestrator _orchestrator;
    private readonly ILogger<NotificationController> _logger;

    public NotificationController(
        SentinelGateDbContext db,
        NotificationOrchestrator orchestrator,
        ILogger<NotificationController> logger)
    {
        _db = db;
        _orchestrator = orchestrator;
        _logger = logger;
    }

    /// <summary>
    /// Send a test notification through all configured channels.
    /// </summary>
    [HttpPost("send")]
    public async Task<IActionResult> Send([FromBody] SendNotificationRequest request)
    {
        var alertEvent = new AlertEvent
        {
            Id = Guid.NewGuid(),
            EventType = request.EventType ?? "test.notification",
            Severity = request.Severity ?? AlertSeverity.Info,
            Details = request.Message ?? "Test notification from SentinelGate",
            ClientIdentity = request.ClientIdentity
        };

        await _orchestrator.ProcessEvent(alertEvent);

        return Ok(new { alertEvent.Id, Status = "dispatched" });
    }

    /// <summary>
    /// List recent alert events with pagination.
    /// </summary>
    [HttpGet("events")]
    public async Task<IActionResult> GetEvents(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var totalCount = await _db.AlertEvents.CountAsync();

        var events = await _db.AlertEvents
            .OrderByDescending(e => e.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return Ok(new
        {
            Data = events,
            Page = page,
            PageSize = pageSize,
            TotalCount = totalCount,
            TotalPages = (int)Math.Ceiling((double)totalCount / pageSize)
        });
    }

    /// <summary>
    /// Acknowledge an alert event.
    /// </summary>
    [HttpPost("events/{id:guid}/acknowledge")]
    public async Task<IActionResult> Acknowledge(Guid id)
    {
        var alertEvent = await _db.AlertEvents.FindAsync(id);
        if (alertEvent is null)
            return NotFound(new { Error = "Alert event not found" });

        if (alertEvent.IsAcknowledged)
            return Ok(new { Message = "Already acknowledged", alertEvent.Id });

        alertEvent.IsAcknowledged = true;
        await _db.SaveChangesAsync();

        _logger.LogInformation("Alert event {EventId} acknowledged", id);
        return Ok(new { Message = "Acknowledged", alertEvent.Id });
    }

    /// <summary>
    /// List all configured webhook subscriptions.
    /// </summary>
    [HttpGet("webhooks")]
    public async Task<IActionResult> GetWebhooks()
    {
        var webhooks = await _db.WebhookSubscriptions
            .OrderByDescending(w => w.CreatedAt)
            .ToListAsync();

        return Ok(webhooks);
    }

    /// <summary>
    /// Register a new webhook subscription.
    /// </summary>
    [HttpPost("webhooks")]
    public async Task<IActionResult> CreateWebhook([FromBody] WebhookRegistrationRequest request)
    {
        var subscription = new WebhookSubscription
        {
            Id = Guid.NewGuid(),
            TenantId = request.TenantId,
            Url = request.Url,
            Events = request.Events,
            Secret = request.Secret,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };

        _db.WebhookSubscriptions.Add(subscription);
        await _db.SaveChangesAsync();

        _logger.LogInformation("Webhook registered: {WebhookId} -> {Url}", subscription.Id, subscription.Url);
        return CreatedAtAction(nameof(GetWebhooks), new { id = subscription.Id }, subscription);
    }

    /// <summary>
    /// Remove a webhook subscription.
    /// </summary>
    [HttpDelete("webhooks/{id:guid}")]
    public async Task<IActionResult> DeleteWebhook(Guid id)
    {
        var webhook = await _db.WebhookSubscriptions.FindAsync(id);
        if (webhook is null)
            return NotFound(new { Error = "Webhook not found" });

        _db.WebhookSubscriptions.Remove(webhook);
        await _db.SaveChangesAsync();

        _logger.LogInformation("Webhook removed: {WebhookId}", id);
        return NoContent();
    }

    /// <summary>
    /// Health check endpoint.
    /// </summary>
    [HttpGet("health")]
    public IActionResult Health()
    {
        return Ok(new
        {
            Status = "healthy",
            Service = "SentinelGate.Notification.Service",
            Timestamp = DateTime.UtcNow
        });
    }
}

public record SendNotificationRequest(
    string? EventType,
    AlertSeverity? Severity,
    string? Message,
    string? ClientIdentity
);
