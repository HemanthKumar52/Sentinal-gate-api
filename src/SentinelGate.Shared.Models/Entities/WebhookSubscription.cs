using System.ComponentModel.DataAnnotations;

namespace SentinelGate.Shared.Models.Entities;

public class WebhookSubscription
{
    [Key]
    public Guid Id { get; set; }

    [Required]
    [MaxLength(128)]
    public string TenantId { get; set; } = string.Empty;

    [Required]
    [MaxLength(2048)]
    [Url]
    public string Url { get; set; } = string.Empty;

    [MaxLength(1024)]
    public string? Events { get; set; }

    [Required]
    [MaxLength(256)]
    public string Secret { get; set; } = string.Empty;

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
