using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using SentinelGate.Shared.Models.Enums;

namespace SentinelGate.Shared.Models.Entities;

public class RequestLog
{
    [Key]
    public Guid Id { get; set; }

    [MaxLength(256)]
    public string? ClientIdentity { get; set; }

    [MaxLength(45)]
    public string? ClientIp { get; set; }

    [MaxLength(128)]
    public string? ApiKey { get; set; }

    [MaxLength(128)]
    public string? TenantId { get; set; }

    [Required]
    [MaxLength(512)]
    public string EndpointPath { get; set; } = string.Empty;

    [Required]
    [MaxLength(10)]
    public string HttpMethod { get; set; } = string.Empty;

    public int ResponseStatusCode { get; set; }

    public double LatencyMs { get; set; }

    public long RequestBodySize { get; set; }

    public long ResponseSize { get; set; }

    [MaxLength(2)]
    public string? GeoCountry { get; set; }

    [MaxLength(512)]
    public string? UserAgent { get; set; }

    public bool IsBlocked { get; set; }

    public bool IsRateLimited { get; set; }

    public RateLimitAlgorithm? RateLimitAlgorithm { get; set; }

    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}
