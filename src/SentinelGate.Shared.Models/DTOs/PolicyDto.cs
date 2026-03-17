using SentinelGate.Shared.Models.Enums;

namespace SentinelGate.Shared.Models.DTOs;

public record PolicyDto(
    Guid Id,
    string Name,
    RateLimitAlgorithm Algorithm,
    int Limit,
    int WindowSeconds,
    int? BurstLimit,
    double? RefillRate,
    int? LeakyCapacity,
    double? LeakyRate,
    string? EndpointPattern,
    string? TenantId,
    int Priority,
    bool IsGlobal,
    bool IsEnabled,
    DateTime CreatedAt,
    DateTime UpdatedAt
);
