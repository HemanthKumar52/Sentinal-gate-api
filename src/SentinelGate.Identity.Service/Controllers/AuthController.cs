using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SentinelGate.Identity.Service.Services;
using SentinelGate.Shared.Models.Enums;

namespace SentinelGate.Identity.Service.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly JwtTokenService _jwtService;
    private readonly TenantService _tenantService;

    public AuthController(JwtTokenService jwtService, TenantService tenantService)
    {
        _jwtService = jwtService;
        _tenantService = tenantService;
    }

    /// <summary>Login and receive a JWT token (demo: accepts any credentials)</summary>
    [HttpPost("login")]
    public IActionResult Login([FromBody] LoginRequest request)
    {
        // Demo mode: accept any username/password combination
        var token = _jwtService.GenerateToken(request.Username, request.Role ?? "developer");

        return Ok(new
        {
            token,
            expiresIn = 3600,
            tokenType = "Bearer"
        });
    }

    /// <summary>Register a new tenant and receive a JWT token</summary>
    [HttpPost("register")]
    public async Task<IActionResult> Register([FromBody] RegisterRequest request)
    {
        var tier = Enum.TryParse<TenantTier>(request.Tier, true, out var parsedTier)
            ? parsedTier
            : TenantTier.Free;

        var tenant = await _tenantService.CreateTenant(request.Name, tier);
        var token = _jwtService.GenerateToken(tenant.Id.ToString(), "developer");

        return CreatedAtAction(nameof(GetMe), new
        {
            tenantId = tenant.Id,
            name = tenant.Name,
            tier = tenant.Tier.ToString(),
            token,
            expiresIn = 3600,
            tokenType = "Bearer"
        });
    }

    /// <summary>Get current user info from JWT</summary>
    [HttpGet("me")]
    [Authorize]
    public IActionResult GetMe()
    {
        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                     ?? User.FindFirst("userId")?.Value;
        var role = User.FindFirst(ClaimTypes.Role)?.Value;

        return Ok(new
        {
            userId,
            role,
            claims = User.Claims.Select(c => new { c.Type, c.Value })
        });
    }
}

public record LoginRequest(string Username, string Password, string? Role = null);
public record RegisterRequest(string Name, string? Tier = null);
