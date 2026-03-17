using System.ComponentModel.DataAnnotations;
using SentinelGate.Shared.Models.Enums;

namespace SentinelGate.Shared.Models.Entities;

public class RatePolicy
{
    [Key]
    public Guid Id { get; set; }

    [Required]
    [MaxLength(128)]
    public string Name { get; set; } = string.Empty;

    [Required]
    public RateLimitAlgorithm Algorithm { get; set; }

    public int Limit { get; set; }

    public int WindowSeconds { get; set; }

    public int? BurstLimit { get; set; }

    public double? RefillRate { get; set; }

    public int? LeakyCapacity { get; set; }

    public double? LeakyRate { get; set; }

    [MaxLength(512)]
    public string? EndpointPattern { get; set; }

    [MaxLength(128)]
    public string? TenantId { get; set; }

    public int Priority { get; set; }

    public bool IsGlobal { get; set; }

    public bool IsEnabled { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
