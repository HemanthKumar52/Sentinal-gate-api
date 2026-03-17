using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SentinelGate.Identity.Service.Services;
using SentinelGate.Shared.Models.Entities;
using SentinelGate.Shared.Models.Enums;

namespace SentinelGate.Identity.Service.Controllers;

[ApiController]
[Route("api/tenants")]
[Authorize]
public class TenantsController : ControllerBase
{
    private readonly TenantService _tenantService;

    public TenantsController(TenantService tenantService)
    {
        _tenantService = tenantService;
    }

    /// <summary>List all tenants</summary>
    [HttpGet]
    public async Task<ActionResult<List<Tenant>>> GetAll()
    {
        var tenants = await _tenantService.GetAllTenants();
        return Ok(tenants);
    }

    /// <summary>Get tenant by ID</summary>
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<Tenant>> GetById(Guid id)
    {
        var tenant = await _tenantService.GetTenant(id);
        if (tenant == null)
            return NotFound(new { message = "Tenant not found" });
        return Ok(tenant);
    }

    /// <summary>Create a new tenant</summary>
    [HttpPost]
    public async Task<ActionResult<Tenant>> Create([FromBody] CreateTenantRequest request)
    {
        var tier = Enum.TryParse<TenantTier>(request.Tier, true, out var parsedTier)
            ? parsedTier
            : TenantTier.Free;

        var tenant = await _tenantService.CreateTenant(request.Name, tier);
        return CreatedAtAction(nameof(GetById), new { id = tenant.Id }, tenant);
    }

    /// <summary>Update a tenant's tier</summary>
    [HttpPut("{id:guid}/tier")]
    public async Task<IActionResult> UpdateTier(Guid id, [FromBody] UpdateTierRequest request)
    {
        if (!Enum.TryParse<TenantTier>(request.Tier, true, out var tier))
            return BadRequest(new { message = "Invalid tier. Valid values: Free, Pro, Enterprise" });

        var success = await _tenantService.UpdateTenantTier(id, tier);
        if (!success)
            return NotFound(new { message = "Tenant not found" });

        return NoContent();
    }
}

public record CreateTenantRequest(string Name, string? Tier = null);
public record UpdateTierRequest(string Tier);
