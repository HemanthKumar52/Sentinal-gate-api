namespace SentinelGate.ThreatDetection.Service.Models;

public enum ThreatSignal
{
    RateLimitViolation,
    High4xxRate,
    AuthFailure,
    SingleEndpointHammering,
    UserAgentAnomaly,
    GeoMismatch,
    PayloadAnomaly
}
