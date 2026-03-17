namespace SentinelGate.Shared.Models.DTOs;

public record ApiKeyDto(
    Guid Id,
    string Name,
    string Key,
    string? Scopes,
    DateTime? ExpiresAt,
    bool IsActive,
    DateTime CreatedAt
);
