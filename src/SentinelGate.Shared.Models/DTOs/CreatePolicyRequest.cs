using System.ComponentModel.DataAnnotations;
using SentinelGate.Shared.Models.Enums;

namespace SentinelGate.Shared.Models.DTOs;

public record CreatePolicyRequest(
    [Required] string Name,
    [Required] RateLimitAlgorithm Algorithm,
    [Required] int Limit,
    [Required] int WindowSeconds,
    int? BurstLimit,
    double? RefillRate,
    int? LeakyCapacity,
    double? LeakyRate,
    string? EndpointPattern,
    string? TenantId,
    int Priority = 0,
    bool IsGlobal = false,
    bool IsEnabled = true
);
