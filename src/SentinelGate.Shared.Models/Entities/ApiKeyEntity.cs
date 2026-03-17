using System.ComponentModel.DataAnnotations;

namespace SentinelGate.Shared.Models.Entities;

public class ApiKeyEntity
{
    [Key]
    public Guid Id { get; set; }

    [Required]
    [MaxLength(128)]
    public string Key { get; set; } = string.Empty;

    [Required]
    [MaxLength(256)]
    public string HashedKey { get; set; } = string.Empty;

    [Required]
    [MaxLength(128)]
    public string Name { get; set; } = string.Empty;

    [Required]
    [MaxLength(128)]
    public string TenantId { get; set; } = string.Empty;

    [MaxLength(256)]
    public string? UserId { get; set; }

    [MaxLength(1024)]
    public string? Scopes { get; set; }

    public int? RateLimitOverride { get; set; }

    public DateTime? ExpiresAt { get; set; }

    public Guid? RotatedFromKeyId { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? RevokedAt { get; set; }
}
