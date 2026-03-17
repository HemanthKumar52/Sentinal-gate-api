using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SentinelGate.Shared.Infrastructure.Data;
using SentinelGate.Shared.Infrastructure.Redis;
using SentinelGate.Shared.Models.DTOs;
using SentinelGate.Shared.Models.Entities;

namespace SentinelGate.Gateway.API.Controllers;

/// <summary>
/// Administrative endpoints for managing rate policies, block lists,
/// audit logs, and threat scores.
/// </summary>
[ApiController]
[Route("api/admin")]
[Produces("application/json")]
public class AdminController : ControllerBase
{
    private readonly SentinelGateDbContext _dbContext;
    private readonly RedisConnectionManager _redis;
    private readonly ILogger<AdminController> _logger;

    public AdminController(
        SentinelGateDbContext dbContext,
        RedisConnectionManager redis,
        ILogger<AdminController> logger)
    {
        _dbContext = dbContext;
        _redis = redis;
        _logger = logger;
    }

    // ─── Rate Policies ───────────────────────────────────────────────────

    /// <summary>
    /// List all rate limiting policies with pagination.
    /// </summary>
    /// <param name="page">Page number (1-based, default 1)</param>
    /// <param name="pageSize">Number of items per page (default 20, max 100)</param>
    /// <returns>Paginated list of rate policies</returns>
    [HttpGet("policies")]
    [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetPolicies([FromQuery] int page = 1, [FromQuery] int pageSize = 20)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var totalCount = await _dbContext.RatePolicies.CountAsync();
        var policies = await _dbContext.RatePolicies
            .AsNoTracking()
            .OrderByDescending(p => p.Priority)
            .ThenBy(p => p.Name)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(p => new PolicyDto(
                p.Id, p.Name, p.Algorithm, p.Limit, p.WindowSeconds,
                p.BurstLimit, p.RefillRate, p.LeakyCapacity, p.LeakyRate,
                p.EndpointPattern, p.TenantId, p.Priority,
                p.IsGlobal, p.IsEnabled, p.CreatedAt, p.UpdatedAt))
            .ToListAsync();

        return Ok(new
        {
            data = policies,
            page,
            pageSize,
            totalCount,
            totalPages = (int)Math.Ceiling((double)totalCount / pageSize)
        });
    }

    /// <summary>
    /// Create a new rate limiting policy.
    /// </summary>
    /// <param name="request">Policy creation parameters</param>
    /// <returns>The newly created policy</returns>
    [HttpPost("policies")]
    [ProducesResponseType(typeof(PolicyDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> CreatePolicy([FromBody] CreatePolicyRequest request)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        var policy = new RatePolicy
        {
            Id = Guid.NewGuid(),
            Name = request.Name,
            Algorithm = request.Algorithm,
            Limit = request.Limit,
            WindowSeconds = request.WindowSeconds,
            BurstLimit = request.BurstLimit,
            RefillRate = request.RefillRate,
            LeakyCapacity = request.LeakyCapacity,
            LeakyRate = request.LeakyRate,
            EndpointPattern = request.EndpointPattern,
            TenantId = request.TenantId,
            Priority = request.Priority,
            IsGlobal = request.IsGlobal,
            IsEnabled = request.IsEnabled,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        _dbContext.RatePolicies.Add(policy);
        await _dbContext.SaveChangesAsync();

        await WriteAuditLogAsync("CreatePolicy", "RatePolicy", policy.Id.ToString(),
            $"Created policy '{policy.Name}'");

        var dto = new PolicyDto(
            policy.Id, policy.Name, policy.Algorithm, policy.Limit, policy.WindowSeconds,
            policy.BurstLimit, policy.RefillRate, policy.LeakyCapacity, policy.LeakyRate,
            policy.EndpointPattern, policy.TenantId, policy.Priority,
            policy.IsGlobal, policy.IsEnabled, policy.CreatedAt, policy.UpdatedAt);

        return CreatedAtAction(nameof(GetPolicies), new { id = policy.Id }, dto);
    }

    /// <summary>
    /// Update an existing rate limiting policy.
    /// </summary>
    /// <param name="id">Policy ID</param>
    /// <param name="request">Updated policy parameters</param>
    /// <returns>The updated policy</returns>
    [HttpPut("policies/{id:guid}")]
    [ProducesResponseType(typeof(PolicyDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdatePolicy(Guid id, [FromBody] CreatePolicyRequest request)
    {
        var policy = await _dbContext.RatePolicies.FindAsync(id);
        if (policy == null)
            return NotFound(new { error = "Not Found", message = $"Policy {id} not found" });

        policy.Name = request.Name;
        policy.Algorithm = request.Algorithm;
        policy.Limit = request.Limit;
        policy.WindowSeconds = request.WindowSeconds;
        policy.BurstLimit = request.BurstLimit;
        policy.RefillRate = request.RefillRate;
        policy.LeakyCapacity = request.LeakyCapacity;
        policy.LeakyRate = request.LeakyRate;
        policy.EndpointPattern = request.EndpointPattern;
        policy.TenantId = request.TenantId;
        policy.Priority = request.Priority;
        policy.IsGlobal = request.IsGlobal;
        policy.IsEnabled = request.IsEnabled;
        policy.UpdatedAt = DateTime.UtcNow;

        await _dbContext.SaveChangesAsync();

        await WriteAuditLogAsync("UpdatePolicy", "RatePolicy", policy.Id.ToString(),
            $"Updated policy '{policy.Name}'");

        var dto = new PolicyDto(
            policy.Id, policy.Name, policy.Algorithm, policy.Limit, policy.WindowSeconds,
            policy.BurstLimit, policy.RefillRate, policy.LeakyCapacity, policy.LeakyRate,
            policy.EndpointPattern, policy.TenantId, policy.Priority,
            policy.IsGlobal, policy.IsEnabled, policy.CreatedAt, policy.UpdatedAt);

        return Ok(dto);
    }

    /// <summary>
    /// Delete a rate limiting policy.
    /// </summary>
    /// <param name="id">Policy ID</param>
    /// <returns>No content on success</returns>
    [HttpDelete("policies/{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeletePolicy(Guid id)
    {
        var policy = await _dbContext.RatePolicies.FindAsync(id);
        if (policy == null)
            return NotFound(new { error = "Not Found", message = $"Policy {id} not found" });

        _dbContext.RatePolicies.Remove(policy);
        await _dbContext.SaveChangesAsync();

        await WriteAuditLogAsync("DeletePolicy", "RatePolicy", id.ToString(),
            $"Deleted policy '{policy.Name}'");

        return NoContent();
    }

    // ─── Block List ──────────────────────────────────────────────────────

    /// <summary>
    /// List all blocked clients with pagination.
    /// </summary>
    /// <param name="page">Page number (1-based, default 1)</param>
    /// <param name="pageSize">Number of items per page (default 20, max 100)</param>
    /// <param name="activeOnly">Filter to active blocks only (default true)</param>
    /// <returns>Paginated list of blocked clients</returns>
    [HttpGet("blocklist")]
    [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetBlockList(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] bool activeOnly = true)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var query = _dbContext.BlockedClients.AsNoTracking().Where(b => !b.IsDeleted);
        if (activeOnly)
            query = query.Where(b => b.IsActive);

        var totalCount = await query.CountAsync();
        var items = await query
            .OrderByDescending(b => b.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return Ok(new { data = items, page, pageSize, totalCount, totalPages = (int)Math.Ceiling((double)totalCount / pageSize) });
    }

    /// <summary>
    /// Add a client to the block list.
    /// </summary>
    /// <param name="request">Block client parameters</param>
    /// <returns>The newly created block entry</returns>
    [HttpPost("blocklist")]
    [ProducesResponseType(typeof(BlockedClient), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> AddToBlockList([FromBody] BlockClientRequest request)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        if (string.IsNullOrEmpty(request.ClientIdentity)
            && string.IsNullOrEmpty(request.IpAddress)
            && string.IsNullOrEmpty(request.CidrRange))
        {
            return BadRequest(new { error = "Bad Request", message = "At least one of ClientIdentity, IpAddress, or CidrRange must be provided" });
        }

        var blocked = new BlockedClient
        {
            Id = Guid.NewGuid(),
            ClientIdentity = request.ClientIdentity,
            IpAddress = request.IpAddress,
            CidrRange = request.CidrRange,
            Reason = request.Reason,
            BlockType = request.BlockType,
            ExpiresAt = request.ExpiresAt,
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = HttpContext.Items["ClientIdentity"]?.ToString() ?? "admin"
        };

        _dbContext.BlockedClients.Add(blocked);
        await _dbContext.SaveChangesAsync();

        // Cache in Redis for fast middleware lookup
        var db = _redis.GetDatabase();
        if (db != null)
        {
            try
            {
                var ttl = blocked.ExpiresAt.HasValue
                    ? blocked.ExpiresAt.Value - DateTime.UtcNow
                    : TimeSpan.FromHours(24);

                if (!string.IsNullOrEmpty(blocked.ClientIdentity))
                    await db.StringSetAsync($"blocked:{blocked.ClientIdentity}", blocked.Reason ?? "Blocked", ttl);
                if (!string.IsNullOrEmpty(blocked.IpAddress))
                    await db.StringSetAsync($"blocked:ip:{blocked.IpAddress}", blocked.Reason ?? "Blocked", ttl);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to cache block entry in Redis");
            }
        }

        await WriteAuditLogAsync("AddBlock", "BlockedClient", blocked.Id.ToString(),
            $"Blocked {blocked.ClientIdentity ?? blocked.IpAddress ?? blocked.CidrRange}: {blocked.Reason}");

        return CreatedAtAction(nameof(GetBlockList), new { id = blocked.Id }, blocked);
    }

    /// <summary>
    /// Remove a client from the block list (soft delete).
    /// </summary>
    /// <param name="id">Block entry ID</param>
    /// <returns>No content on success</returns>
    [HttpDelete("blocklist/{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> RemoveFromBlockList(Guid id)
    {
        var blocked = await _dbContext.BlockedClients.FindAsync(id);
        if (blocked == null || blocked.IsDeleted)
            return NotFound(new { error = "Not Found", message = $"Block entry {id} not found" });

        blocked.IsActive = false;
        blocked.IsDeleted = true;
        await _dbContext.SaveChangesAsync();

        // Remove from Redis cache
        var db = _redis.GetDatabase();
        if (db != null)
        {
            try
            {
                if (!string.IsNullOrEmpty(blocked.ClientIdentity))
                    await db.KeyDeleteAsync($"blocked:{blocked.ClientIdentity}");
                if (!string.IsNullOrEmpty(blocked.IpAddress))
                    await db.KeyDeleteAsync($"blocked:ip:{blocked.IpAddress}");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to remove block entry from Redis cache");
            }
        }

        await WriteAuditLogAsync("RemoveBlock", "BlockedClient", id.ToString(),
            $"Unblocked {blocked.ClientIdentity ?? blocked.IpAddress ?? blocked.CidrRange}");

        return NoContent();
    }

    // ─── Audit Log ───────────────────────────────────────────────────────

    /// <summary>
    /// Query the audit log with optional filters.
    /// </summary>
    /// <param name="actor">Filter by actor (partial match)</param>
    /// <param name="action">Filter by action (exact match)</param>
    /// <param name="from">Start date filter (inclusive)</param>
    /// <param name="to">End date filter (inclusive)</param>
    /// <param name="page">Page number (1-based, default 1)</param>
    /// <param name="pageSize">Number of items per page (default 20, max 100)</param>
    /// <returns>Paginated audit log entries</returns>
    [HttpGet("audit-log")]
    [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAuditLog(
        [FromQuery] string? actor = null,
        [FromQuery] string? action = null,
        [FromQuery] DateTime? from = null,
        [FromQuery] DateTime? to = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var query = _dbContext.AuditLogs.AsNoTracking().AsQueryable();

        if (!string.IsNullOrEmpty(actor))
            query = query.Where(a => a.Actor.Contains(actor));
        if (!string.IsNullOrEmpty(action))
            query = query.Where(a => a.Action == action);
        if (from.HasValue)
            query = query.Where(a => a.Timestamp >= from.Value);
        if (to.HasValue)
            query = query.Where(a => a.Timestamp <= to.Value);

        var totalCount = await query.CountAsync();
        var items = await query
            .OrderByDescending(a => a.Timestamp)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return Ok(new { data = items, page, pageSize, totalCount, totalPages = (int)Math.Ceiling((double)totalCount / pageSize) });
    }

    // ─── Threat Scores ───────────────────────────────────────────────────

    /// <summary>
    /// List clients sorted by threat score (descending).
    /// </summary>
    /// <param name="page">Page number (1-based, default 1)</param>
    /// <param name="pageSize">Number of items per page (default 20, max 100)</param>
    /// <returns>Paginated list of threat scores</returns>
    [HttpGet("threat-scores")]
    [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetThreatScores([FromQuery] int page = 1, [FromQuery] int pageSize = 20)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var totalCount = await _dbContext.ThreatScores.CountAsync();
        var items = await _dbContext.ThreatScores
            .AsNoTracking()
            .OrderByDescending(t => t.Score)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return Ok(new { data = items, page, pageSize, totalCount, totalPages = (int)Math.Ceiling((double)totalCount / pageSize) });
    }

    /// <summary>
    /// Reset a client's threat score to zero.
    /// </summary>
    /// <param name="clientId">The client identity string</param>
    /// <returns>The reset threat score entry</returns>
    [HttpPost("threat-scores/{clientId}/reset")]
    [ProducesResponseType(typeof(ThreatScore), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ResetThreatScore(string clientId)
    {
        var decoded = Uri.UnescapeDataString(clientId);
        var threatScore = await _dbContext.ThreatScores
            .FirstOrDefaultAsync(t => t.ClientIdentity == decoded);

        if (threatScore == null)
            return NotFound(new { error = "Not Found", message = $"Threat score for client '{decoded}' not found" });

        threatScore.Score = 0;
        threatScore.RateLimitViolations = 0;
        threatScore.High4xxRate = 0;
        threatScore.AuthFailures = 0;
        threatScore.SingleEndpointHammering = 0;
        threatScore.UserAgentAnomaly = 0;
        threatScore.GeoMismatch = 0;
        threatScore.PayloadAnomaly = 0;
        threatScore.LastUpdated = DateTime.UtcNow;
        threatScore.LastDecayed = DateTime.UtcNow;

        await _dbContext.SaveChangesAsync();

        await WriteAuditLogAsync("ResetThreatScore", "ThreatScore", threatScore.Id.ToString(),
            $"Reset threat score for '{decoded}'");

        return Ok(threatScore);
    }

    // ─── Helpers ─────────────────────────────────────────────────────────

    private async Task WriteAuditLogAsync(string action, string resource, string? resourceId, string? details)
    {
        var auditLog = new AuditLog
        {
            Id = Guid.NewGuid(),
            Actor = HttpContext.Items["ClientIdentity"]?.ToString() ?? "system",
            Action = action,
            Resource = resource,
            ResourceId = resourceId,
            Details = details,
            IpAddress = HttpContext.Items["ClientIp"]?.ToString(),
            Timestamp = DateTime.UtcNow
        };

        _dbContext.AuditLogs.Add(auditLog);
        await _dbContext.SaveChangesAsync();
    }
}
