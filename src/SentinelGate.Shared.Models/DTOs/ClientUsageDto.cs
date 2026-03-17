namespace SentinelGate.Shared.Models.DTOs;

public record ClientUsageDto(
    string ClientIdentity,
    long TotalRequests,
    long QuotaUsed,
    long QuotaLimit,
    double ErrorRate
);
