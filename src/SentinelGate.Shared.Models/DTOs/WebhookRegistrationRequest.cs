using System.ComponentModel.DataAnnotations;

namespace SentinelGate.Shared.Models.DTOs;

public record WebhookRegistrationRequest(
    [Required] string TenantId,
    [Required][Url] string Url,
    string? Events,
    [Required] string Secret
);
