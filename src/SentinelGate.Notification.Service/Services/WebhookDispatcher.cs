using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SentinelGate.Shared.Models.Entities;

namespace SentinelGate.Notification.Service.Services;

public class WebhookDispatcher
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<WebhookDispatcher> _logger;

    private static readonly int MaxRetries = 3;
    private static readonly int[] BackoffSeconds = { 1, 2, 4 };

    public WebhookDispatcher(HttpClient httpClient, ILogger<WebhookDispatcher> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<bool> DispatchAsync(WebhookSubscription subscription, object payload)
    {
        var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });

        var signature = ComputeHmacSha256(json, subscription.Secret);

        for (int attempt = 1; attempt <= MaxRetries; attempt++)
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, subscription.Url);
                request.Content = new StringContent(json, Encoding.UTF8, "application/json");
                request.Headers.Add("X-Webhook-Signature", signature);
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

                var response = await _httpClient.SendAsync(request);

                if (response.IsSuccessStatusCode)
                {
                    _logger.LogInformation(
                        "Webhook dispatched successfully to {Url} on attempt {Attempt}",
                        subscription.Url, attempt);
                    return true;
                }

                _logger.LogWarning(
                    "Webhook to {Url} returned {StatusCode} on attempt {Attempt}/{MaxRetries}",
                    subscription.Url, (int)response.StatusCode, attempt, MaxRetries);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Webhook to {Url} failed on attempt {Attempt}/{MaxRetries}",
                    subscription.Url, attempt, MaxRetries);
            }

            if (attempt < MaxRetries)
            {
                await Task.Delay(TimeSpan.FromSeconds(BackoffSeconds[attempt - 1]));
            }
        }

        _logger.LogError("Webhook to {Url} failed after {MaxRetries} attempts", subscription.Url, MaxRetries);
        return false;
    }

    private static string ComputeHmacSha256(string payload, string secret)
    {
        var keyBytes = Encoding.UTF8.GetBytes(secret);
        var payloadBytes = Encoding.UTF8.GetBytes(payload);

        using var hmac = new HMACSHA256(keyBytes);
        var hash = hmac.ComputeHash(payloadBytes);
        return $"sha256={Convert.ToHexString(hash).ToLowerInvariant()}";
    }
}
