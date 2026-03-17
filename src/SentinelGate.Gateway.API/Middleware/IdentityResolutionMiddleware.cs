using System.IdentityModel.Tokens.Jwt;
using Microsoft.EntityFrameworkCore;
using SentinelGate.Shared.Infrastructure.Data;

namespace SentinelGate.Gateway.API.Middleware;

/// <summary>
/// Extracts client identity from API key header, JWT bearer token, or falls back to IP address.
/// Sets HttpContext items: ClientIdentity, ClientIp, ApiKey, TenantId.
/// </summary>
public class IdentityResolutionMiddleware : IMiddleware
{
    private readonly ILogger<IdentityResolutionMiddleware> _logger;

    public IdentityResolutionMiddleware(ILogger<IdentityResolutionMiddleware> logger)
    {
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context, RequestDelegate next)
    {
        var clientIp = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        context.Items["ClientIp"] = clientIp;

        string? clientIdentity = null;
        string? apiKey = null;
        string? tenantId = null;

        // 1. Check X-API-Key header
        if (context.Request.Headers.TryGetValue("X-API-Key", out var apiKeyHeader)
            && !string.IsNullOrWhiteSpace(apiKeyHeader.ToString()))
        {
            apiKey = apiKeyHeader.ToString().Trim();
            clientIdentity = $"apikey:{apiKey}";

            // Resolve tenant from API key via DB
            var dbContext = context.RequestServices.GetRequiredService<SentinelGateDbContext>();
            var keyEntity = await dbContext.ApiKeys
                .AsNoTracking()
                .FirstOrDefaultAsync(k => k.Key == apiKey && k.IsActive);

            if (keyEntity != null)
            {
                tenantId = keyEntity.TenantId;
                clientIdentity = $"apikey:{apiKey}:{tenantId}";
            }
        }
        // 2. Check Authorization Bearer JWT
        else if (context.Request.Headers.TryGetValue("Authorization", out var authHeader)
                 && authHeader.ToString().StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            var token = authHeader.ToString()["Bearer ".Length..].Trim();
            try
            {
                var handler = new JwtSecurityTokenHandler();
                if (handler.CanReadToken(token))
                {
                    var jwt = handler.ReadJwtToken(token);
                    var sub = jwt.Claims.FirstOrDefault(c => c.Type == "sub")?.Value;
                    tenantId = jwt.Claims.FirstOrDefault(c => c.Type == "tenant_id")?.Value;

                    if (!string.IsNullOrEmpty(sub))
                    {
                        clientIdentity = $"jwt:{sub}";
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to parse JWT token for identity resolution");
            }
        }

        // 3. Fallback to IP address
        clientIdentity ??= $"ip:{clientIp}";

        context.Items["ClientIdentity"] = clientIdentity;
        context.Items["ApiKey"] = apiKey;
        context.Items["TenantId"] = tenantId;

        _logger.LogDebug("Resolved identity: {ClientIdentity}, IP: {ClientIp}, Tenant: {TenantId}",
            clientIdentity, clientIp, tenantId);

        await next(context);
    }
}
