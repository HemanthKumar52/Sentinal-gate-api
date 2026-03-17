using System.ComponentModel.DataAnnotations;
using SentinelGate.Shared.Models.Enums;

namespace SentinelGate.Shared.Models.Entities;

public class BlockedClient
{
    [Key]
    public Guid Id { get; set; }

    [MaxLength(256)]
    public string? ClientIdentity { get; set; }

    [MaxLength(45)]
    public string? IpAddress { get; set; }

    [MaxLength(43)]
    public string? CidrRange { get; set; }

    [MaxLength(512)]
    public string? Reason { get; set; }

    [Required]
    public BlockType BlockType { get; set; }

    public double ThreatScore { get; set; }

    public DateTime? ExpiresAt { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [MaxLength(256)]
    public string? CreatedBy { get; set; }

    public bool IsDeleted { get; set; }
}
