using System.ComponentModel.DataAnnotations;

namespace SentinelGate.Shared.Models.Entities;

public class DailyAggregate
{
    [Key]
    public Guid Id { get; set; }

    [Required]
    [MaxLength(512)]
    public string EndpointPath { get; set; } = string.Empty;

    public DateOnly Date { get; set; }

    public long RequestCount { get; set; }

    public long ErrorCount { get; set; }

    public double AvgLatencyMs { get; set; }

    public double P50LatencyMs { get; set; }

    public double P95LatencyMs { get; set; }

    public double P99LatencyMs { get; set; }

    [MaxLength(256)]
    public string? TopClientIdentity { get; set; }

    public long TopClientRequests { get; set; }
}
