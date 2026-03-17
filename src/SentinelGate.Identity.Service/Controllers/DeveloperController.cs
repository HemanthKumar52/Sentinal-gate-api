using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SentinelGate.Identity.Service.Services;
using SentinelGate.Shared.Infrastructure.Data;
using SentinelGate.Shared.Models.DTOs;
using SentinelGate.Shared.Models.Entities;

namespace SentinelGate.Identity.Service.Controllers;

[ApiController]
[Route("api/developer")]
[Authorize]
public class DeveloperController : ControllerBase
{
    private readonly ApiKeyService _apiKeyService;
    private readonly TenantService _tenantService;
    private readonly SentinelGateDbContext _db;

    public DeveloperController(
        ApiKeyService apiKeyService,
        TenantService tenantService,
        SentinelGateDbContext db)
    {
        _apiKeyService = apiKeyService;
        _tenantService = tenantService;
        _db = db;
    }

    private string GetTenantId()
    {
        return User.FindFirst("userId")?.Value ?? "unknown";
    }

    /// <summary>List my API keys</summary>
    [HttpGet("keys")]
    public async Task<ActionResult<List<ApiKeyDto>>> GetKeys()
    {
        var keys = await _apiKeyService.GetKeys(GetTenantId());
        return Ok(keys);
    }

    /// <summary>Generate a new API key</summary>
    [HttpPost("keys")]
    public async Task<ActionResult<ApiKeyDto>> CreateKey([FromBody] CreateApiKeyRequest request)
    {
        // Override tenantId with the authenticated user's identity
        var tenantId = GetTenantId();
        var overridden = request with { TenantId = tenantId };
        var key = await _apiKeyService.GenerateKey(overridden);
        return CreatedAtAction(nameof(GetKeys), key);
    }

    /// <summary>Rotate an API key</summary>
    [HttpPost("keys/{id}/rotate")]
    public async Task<ActionResult<ApiKeyDto>> RotateKey(Guid id)
    {
        var result = await _apiKeyService.RotateKey(id);
        if (result == null)
            return NotFound(new { message = "Key not found or already revoked" });
        return Ok(result);
    }

    /// <summary>Revoke an API key</summary>
    [HttpDelete("keys/{id}")]
    public async Task<IActionResult> RevokeKey(Guid id)
    {
        var success = await _apiKeyService.RevokeKey(id);
        if (!success)
            return NotFound(new { message = "Key not found" });
        return NoContent();
    }

    /// <summary>Get my usage stats</summary>
    [HttpGet("usage")]
    public async Task<ActionResult<ClientUsageDto>> GetUsage()
    {
        var tenantId = GetTenantId();
        if (!Guid.TryParse(tenantId, out var tenantGuid))
            return BadRequest(new { message = "Invalid tenant identity" });

        var usage = await _tenantService.GetTenantUsage(tenantGuid);
        return Ok(usage);
    }

    /// <summary>List webhook subscriptions</summary>
    [HttpGet("webhooks")]
    public async Task<ActionResult<List<WebhookSubscription>>> GetWebhooks()
    {
        var tenantId = GetTenantId();
        var webhooks = await _db.WebhookSubscriptions
            .Where(w => w.TenantId == tenantId && w.IsActive)
            .OrderByDescending(w => w.CreatedAt)
            .ToListAsync();
        return Ok(webhooks);
    }

    /// <summary>Register a webhook subscription</summary>
    [HttpPost("webhooks")]
    public async Task<ActionResult<WebhookSubscription>> RegisterWebhook(
        [FromBody] WebhookRegistrationRequest request)
    {
        var tenantId = GetTenantId();
        var webhook = new WebhookSubscription
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Url = request.Url,
            Events = request.Events,
            Secret = request.Secret,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };

        _db.WebhookSubscriptions.Add(webhook);
        await _db.SaveChangesAsync();
        return CreatedAtAction(nameof(GetWebhooks), webhook);
    }
}
