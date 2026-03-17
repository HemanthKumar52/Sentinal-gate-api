using System.Text;
using System.Text.Json;

namespace SentinelGate.Notification.Service.Services;

public class SlackNotifier
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<SlackNotifier> _logger;

    public SlackNotifier(HttpClient httpClient, ILogger<SlackNotifier> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<bool> SendAsync(string webhookUrl, string message, string? channel = null)
    {
        try
        {
            var blocks = new List<object>
            {
                new
                {
                    type = "header",
                    text = new
                    {
                        type = "plain_text",
                        text = "SentinelGate Alert",
                        emoji = true
                    }
                },
                new
                {
                    type = "section",
                    fields = new object[]
                    {
                        new
                        {
                            type = "mrkdwn",
                            text = $"*Details:*\n{message}"
                        },
                        new
                        {
                            type = "mrkdwn",
                            text = $"*Timestamp:*\n{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC"
                        }
                    }
                }
            };

            var payload = new Dictionary<string, object>
            {
                ["blocks"] = blocks,
                ["text"] = message
            };

            if (!string.IsNullOrWhiteSpace(channel))
            {
                payload["channel"] = channel;
            }

            var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });

            var content = new StringContent(json, Encoding.UTF8, "application/json");
            var response = await _httpClient.PostAsync(webhookUrl, content);

            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation("Slack notification sent successfully to {Channel}", channel ?? "default");
                return true;
            }

            _logger.LogWarning("Slack notification failed with status {StatusCode}", (int)response.StatusCode);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send Slack notification");
            return false;
        }
    }
}
