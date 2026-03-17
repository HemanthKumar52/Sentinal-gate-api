using System.ComponentModel.DataAnnotations;

namespace SentinelGate.Shared.Models.DTOs;

public record CreateApiKeyRequest(
    [Required] string Name,
    [Required] string TenantId,
    string? UserId,
    string? Scopes,
    int? RateLimitOverride,
    DateTime? ExpiresAt
);
