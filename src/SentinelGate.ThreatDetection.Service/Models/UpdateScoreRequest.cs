using System.ComponentModel.DataAnnotations;

namespace SentinelGate.ThreatDetection.Service.Models;

public record UpdateScoreRequest(
    [Required] string ClientIdentity,
    [Required] string IpAddress,
    [Required] ThreatSignal Signal
);
