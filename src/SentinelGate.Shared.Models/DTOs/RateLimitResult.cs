using SentinelGate.Shared.Models.Enums;

namespace SentinelGate.Shared.Models.DTOs;

public record RateLimitResult(
    bool IsAllowed,
    int Remaining,
    int Limit,
    DateTime ResetAt,
    TimeSpan? RetryAfter,
    RateLimitAlgorithm Algorithm,
    string ClientIdentity
);
