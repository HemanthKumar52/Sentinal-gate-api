namespace SentinelGate.Shared.Models.DTOs;

public record DashboardMetricsDto(
    double RequestsPerSecond,
    int ActiveClients,
    int BlockedClients,
    double AverageLatencyMs,
    double ErrorRate,
    List<ClientUsageDto> TopClients
);
