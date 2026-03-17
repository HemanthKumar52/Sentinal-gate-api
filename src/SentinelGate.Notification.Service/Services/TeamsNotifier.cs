using System.Text;
using System.Text.Json;

namespace SentinelGate.Notification.Service.Services;

public class TeamsNotifier
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<TeamsNotifier> _logger;

    public TeamsNotifier(HttpClient httpClient, ILogger<TeamsNotifier> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<bool> SendAsync(string webhookUrl, string message)
    {
        try
        {
            var card = new
            {
                type = "message",
                attachments = new[]
                {
                    new
                    {
                        contentType = "application/vnd.microsoft.card.adaptive",
                        content = new
                        {
                            type = "AdaptiveCard",
                            version = "1.4",
                            body = new object[]
                            {
                                new
                                {
                                    type = "TextBlock",
                                    size = "Large",
                                    weight = "Bolder",
                                    text = "SentinelGate Alert"
                                },
                                new
                                {
                                    type = "TextBlock",
                                    text = message,
                                    wrap = true
                                },
                                new
                                {
                                    type = "FactSet",
                                    facts = new[]
                                    {
                                        new { title = "Source", value = "SentinelGate" },
                                        new { title = "Time", value = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss") + " UTC" }
                                    }
                                }
                            }
                        }
                    }
                }
            };

            var json = JsonSerializer.Serialize(card, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });

            var content = new StringContent(json, Encoding.UTF8, "application/json");
            var response = await _httpClient.PostAsync(webhookUrl, content);

            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation("Teams notification sent successfully");
                return true;
            }

            _logger.LogWarning("Teams notification failed with status {StatusCode}", (int)response.StatusCode);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send Teams notification");
            return false;
        }
    }
}
