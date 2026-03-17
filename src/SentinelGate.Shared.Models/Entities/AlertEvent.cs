using System.ComponentModel.DataAnnotations;
using SentinelGate.Shared.Models.Enums;

namespace SentinelGate.Shared.Models.Entities;

public class AlertEvent
{
    [Key]
    public Guid Id { get; set; }

    [Required]
    [MaxLength(128)]
    public string EventType { get; set; } = string.Empty;

    [MaxLength(256)]
    public string? ClientIdentity { get; set; }

    [MaxLength(2048)]
    public string? Details { get; set; }

    [Required]
    public AlertSeverity Severity { get; set; }

    public bool IsAcknowledged { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
