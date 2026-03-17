namespace SentinelGate.Shared.Models.DTOs;

public record EndpointStatDto(
    string Path,
    long RequestCount,
    long ErrorCount,
    double AvgLatencyMs
);
