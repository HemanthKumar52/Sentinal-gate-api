using System.ComponentModel.DataAnnotations;

namespace SentinelGate.Shared.Models.Entities;

public class AuditLog
{
    [Key]
    public Guid Id { get; set; }

    [Required]
    [MaxLength(256)]
    public string Actor { get; set; } = string.Empty;

    [Required]
    [MaxLength(128)]
    public string Action { get; set; } = string.Empty;

    [Required]
    [MaxLength(256)]
    public string Resource { get; set; } = string.Empty;

    [MaxLength(128)]
    public string? ResourceId { get; set; }

    [MaxLength(2048)]
    public string? Details { get; set; }

    [MaxLength(45)]
    public string? IpAddress { get; set; }

    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}
