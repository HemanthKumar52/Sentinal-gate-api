using SentinelGate.Shared.Models.Enums;

namespace SentinelGate.Shared.Models.DTOs;

public record ThreatScoreResult(
    string ClientIdentity,
    double Score,
    ThreatAction Action,
    List<string> Triggers
);
