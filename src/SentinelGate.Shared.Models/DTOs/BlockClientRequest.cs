using System.ComponentModel.DataAnnotations;
using SentinelGate.Shared.Models.Enums;

namespace SentinelGate.Shared.Models.DTOs;

public record BlockClientRequest(
    string? ClientIdentity,
    string? IpAddress,
    string? CidrRange,
    [Required] string Reason,
    BlockType BlockType = BlockType.Manual,
    DateTime? ExpiresAt = null
);
