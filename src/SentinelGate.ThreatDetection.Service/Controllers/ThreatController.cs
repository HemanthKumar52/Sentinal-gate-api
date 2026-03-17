using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SentinelGate.Shared.Infrastructure.Data;
using SentinelGate.Shared.Models.DTOs;
using SentinelGate.Shared.Models.Entities;
using SentinelGate.Shared.Models.Enums;
using SentinelGate.ThreatDetection.Service.Models;
using SentinelGate.ThreatDetection.Service.Services;

namespace SentinelGate.ThreatDetection.Service.Controllers;

[ApiController]
[Route("api/threat")]
public class ThreatController : ControllerBase
{
    private readonly ThreatScoringEngine _scoringEngine;
    private readonly SentinelGateDbContext _db;
    private readonly ILogger<ThreatController> _logger;

    public ThreatController(
        ThreatScoringEngine scoringEngine,
        SentinelGateDbContext db,
        ILogger<ThreatController> logger)
    {
        _scoringEngine = scoringEngine;
        _db = db;
        _logger = logger;
    }

    // ─── Score endpoints ──────────────────────────────────────────────────────

    /// <summary>
    /// Updates and returns the threat score for a client based on the received signal.
    /// </summary>
    [HttpPost("score/update")]
    public async Task<ActionResult<ThreatScoreResult>> UpdateScore([FromBody] UpdateScoreRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.ClientIdentity))
            return BadRequest("ClientIdentity is required.");

        if (string.IsNullOrWhiteSpace(request.IpAddress))
            return BadRequest("IpAddress is required.");

        var result = await _scoringEngine.UpdateScore(request.ClientIdentity, request.IpAddress, request.Signal);
        return Ok(result);
    }

    /// <summary>
    /// Gets the current threat score for a client.
    /// </summary>
    [HttpGet("score/{clientIdentity}")]
    public async Task<ActionResult<ThreatScoreResult>> GetScore(string clientIdentity)
    {
        var result = await _scoringEngine.GetScore(clientIdentity);
        if (result == null)
            return NotFound(new { message = $"No threat score found for '{clientIdentity}'." });

        return Ok(result);
    }

    /// <summary>
    /// Resets a client's threat score to zero.
    /// </summary>
    [HttpPost("score/{clientIdentity}/reset")]
    public async Task<IActionResult> ResetScore(string clientIdentity)
    {
        await _scoringEngine.ResetScore(clientIdentity);
        return Ok(new { message = $"Threat score reset for '{clientIdentity}'." });
    }

    /// <summary>
    /// Lists all threat scores ordered by score descending, with pagination.
    /// </summary>
    [HttpGet("scores")]
    public async Task<ActionResult> GetAllScores(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var query = _db.ThreatScores
            .AsNoTracking()
            .OrderByDescending(t => t.Score);

        var totalCount = await query.CountAsync();
        var items = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return Ok(new
        {
            page,
            pageSize,
            totalCount,
            totalPages = (int)Math.Ceiling(totalCount / (double)pageSize),
            items
        });
    }

    // ─── Blocklist endpoints ──────────────────────────────────────────────────

    /// <summary>
    /// Lists all blocked clients.
    /// </summary>
    [HttpGet("blocklist")]
    public async Task<ActionResult> GetBlocklist(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25,
        [FromQuery] bool activeOnly = true)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var query = _db.BlockedClients
            .AsNoTracking()
            .Where(b => !b.IsDeleted);

        if (activeOnly)
            query = query.Where(b => b.IsActive);

        query = query.OrderByDescending(b => b.CreatedAt);

        var totalCount = await query.CountAsync();
        var items = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return Ok(new
        {
            page,
            pageSize,
            totalCount,
            totalPages = (int)Math.Ceiling(totalCount / (double)pageSize),
            items
        });
    }

    /// <summary>
    /// Manually blocks a client.
    /// </summary>
    [HttpPost("blocklist")]
    public async Task<ActionResult<BlockedClient>> BlockClient([FromBody] BlockClientRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.ClientIdentity) &&
            string.IsNullOrWhiteSpace(request.IpAddress) &&
            string.IsNullOrWhiteSpace(request.CidrRange))
        {
            return BadRequest("At least one of ClientIdentity, IpAddress, or CidrRange is required.");
        }

        var blocked = new BlockedClient
        {
            Id = Guid.NewGuid(),
            ClientIdentity = request.ClientIdentity,
            IpAddress = request.IpAddress,
            CidrRange = request.CidrRange,
            Reason = request.Reason,
            BlockType = request.BlockType,
            ThreatScore = 0,
            ExpiresAt = request.ExpiresAt,
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "Manual"
        };

        _db.BlockedClients.Add(blocked);
        await _db.SaveChangesAsync();

        _logger.LogInformation("Client manually blocked: {Identity}/{Ip} - {Reason}",
            request.ClientIdentity, request.IpAddress, request.Reason);

        return CreatedAtAction(nameof(GetBlocklist), blocked);
    }

    /// <summary>
    /// Unblocks a client by ID (soft delete).
    /// </summary>
    [HttpDelete("blocklist/{id:guid}")]
    public async Task<IActionResult> UnblockClient(Guid id)
    {
        var blocked = await _db.BlockedClients.FindAsync(id);
        if (blocked == null || blocked.IsDeleted)
            return NotFound(new { message = $"Blocked client '{id}' not found." });

        blocked.IsActive = false;
        blocked.IsDeleted = true;
        await _db.SaveChangesAsync();

        _logger.LogInformation("Client unblocked: {Id} ({Identity})", id, blocked.ClientIdentity);

        return Ok(new { message = $"Client '{id}' unblocked." });
    }

    /// <summary>
    /// Imports a block list from a JSON array of BlockClientRequest.
    /// </summary>
    [HttpPost("blocklist/import")]
    public async Task<ActionResult> ImportBlocklist([FromBody] List<BlockClientRequest> requests)
    {
        if (requests == null || requests.Count == 0)
            return BadRequest("Request body must contain at least one entry.");

        var imported = new List<BlockedClient>();

        foreach (var req in requests)
        {
            if (string.IsNullOrWhiteSpace(req.ClientIdentity) &&
                string.IsNullOrWhiteSpace(req.IpAddress) &&
                string.IsNullOrWhiteSpace(req.CidrRange))
            {
                continue; // Skip invalid entries
            }

            var blocked = new BlockedClient
            {
                Id = Guid.NewGuid(),
                ClientIdentity = req.ClientIdentity,
                IpAddress = req.IpAddress,
                CidrRange = req.CidrRange,
                Reason = req.Reason,
                BlockType = req.BlockType,
                ThreatScore = 0,
                ExpiresAt = req.ExpiresAt,
                IsActive = true,
                CreatedAt = DateTime.UtcNow,
                CreatedBy = "Import"
            };

            imported.Add(blocked);
        }

        _db.BlockedClients.AddRange(imported);
        await _db.SaveChangesAsync();

        _logger.LogInformation("Imported {Count} blocked clients", imported.Count);

        return Ok(new { imported = imported.Count, skipped = requests.Count - imported.Count });
    }

    /// <summary>
    /// Exports the active block list as a JSON array.
    /// </summary>
    [HttpGet("blocklist/export")]
    public async Task<ActionResult> ExportBlocklist()
    {
        var blocklist = await _db.BlockedClients
            .AsNoTracking()
            .Where(b => b.IsActive && !b.IsDeleted)
            .OrderByDescending(b => b.CreatedAt)
            .ToListAsync();

        return Ok(blocklist);
    }

    // ─── Health ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Health check endpoint.
    /// </summary>
    [HttpGet("health")]
    public IActionResult Health()
    {
        return Ok(new
        {
            status = "healthy",
            service = "SentinelGate.ThreatDetection.Service",
            timestamp = DateTime.UtcNow
        });
    }
}
