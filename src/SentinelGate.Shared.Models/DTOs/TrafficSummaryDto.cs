namespace SentinelGate.Shared.Models.DTOs;

public record TrafficSummaryDto(
    long TotalRequests,
    long BlockedRequests,
    long RateLimitedRequests,
    double AverageLatencyMs,
    double ErrorRate,
    List<EndpointStatDto> TopEndpoints,
    string Period
);
