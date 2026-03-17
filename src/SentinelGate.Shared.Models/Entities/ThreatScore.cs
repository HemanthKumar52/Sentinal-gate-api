using System.ComponentModel.DataAnnotations;

namespace SentinelGate.Shared.Models.Entities;

public class ThreatScore
{
    [Key]
    public Guid Id { get; set; }

    [Required]
    [MaxLength(256)]
    public string ClientIdentity { get; set; } = string.Empty;

    [MaxLength(45)]
    public string? IpAddress { get; set; }

    public double Score { get; set; }

    public int RateLimitViolations { get; set; }

    public double High4xxRate { get; set; }

    public int AuthFailures { get; set; }

    public double SingleEndpointHammering { get; set; }

    public double UserAgentAnomaly { get; set; }

    public double GeoMismatch { get; set; }

    public double PayloadAnomaly { get; set; }

    public DateTime LastUpdated { get; set; } = DateTime.UtcNow;

    public DateTime LastDecayed { get; set; } = DateTime.UtcNow;
}
