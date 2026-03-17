using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SentinelGate.Shared.Infrastructure.Data;
using SentinelGate.Shared.Models.Configuration;
using SentinelGate.Shared.Models.Entities;

namespace SentinelGate.Gateway.API.Middleware;

/// <summary>
/// After the response is generated, checks for negative signals (4xx responses, rate limit hits,
/// auth failures) and updates the client's threat score accordingly.
/// Publishes score updates asynchronously to avoid blocking the response.
/// </summary>
public class ThreatScoreMiddleware : IMiddleware
{
    private readonly ILogger<ThreatScoreMiddleware> _logger;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly SentinelGateOptions _options;

    public ThreatScoreMiddleware(
        ILogger<ThreatScoreMiddleware> logger,
        IHttpClientFactory httpClientFactory,
        IOptions<SentinelGateOptions> options)
    {
        _logger = logger;
        _httpClientFactory = httpClientFactory;
        _options = options.Value;
    }

    public async Task InvokeAsync(HttpContext context, RequestDelegate next)
    {
        await next(context);

        // Only process if threat detection is enabled
        if (!_options.ThreatDetection.Enabled)
            return;

        var clientIdentity = context.Items["ClientIdentity"]?.ToString();
        var clientIp = context.Items["ClientIp"]?.ToString();
        var statusCode = context.Response.StatusCode;

        if (string.IsNullOrEmpty(clientIdentity))
            return;

        // Detect negative signals
        var signals = new List<(string Signal, int Weight)>();

        // 4xx responses (client errors)
        if (statusCode >= 400 && statusCode < 500)
        {
            signals.Add(("High4xxRate", _options.ThreatDetection.High4xxRateWeight));
        }

        // Rate limit violations (429)
        if (statusCode == 429 || (context.Items.ContainsKey("IsRateLimited") && (bool)context.Items["IsRateLimited"]!))
        {
            signals.Add(("RateLimitViolation", _options.ThreatDetection.RateLimitViolationWeight));
        }

        // Authentication failures (401)
        if (statusCode == 401)
        {
            signals.Add(("AuthFailure", _options.ThreatDetection.AuthFailureWeight));
        }

        // Forbidden (403) from block list — already blocked, additional weight
        if (statusCode == 403)
        {
            signals.Add(("BlockedAccess", _options.ThreatDetection.High4xxRateWeight));
        }

        if (signals.Count == 0)
            return;

        // Fire-and-forget async update to avoid blocking the response pipeline
        _ = Task.Run(async () =>
        {
            try
            {
                await UpdateThreatScoreAsync(clientIdentity, clientIp, signals, context.RequestServices);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to update threat score for {ClientIdentity}", clientIdentity);
            }
        });
    }

    private async Task UpdateThreatScoreAsync(
        string clientIdentity,
        string? clientIp,
        List<(string Signal, int Weight)> signals,
        IServiceProvider serviceProvider)
    {
        // Try HTTP call to ThreatDetection.Service first
        try
        {
            var client = _httpClientFactory.CreateClient("ThreatDetection");
            var response = await client.PostAsJsonAsync("/api/threat/evaluate", new
            {
                clientIdentity,
                ipAddress = clientIp,
                signals = signals.Select(s => new { signal = s.Signal, weight = s.Weight })
            });

            if (response.IsSuccessStatusCode)
            {
                _logger.LogDebug("Threat score updated via ThreatDetection.Service for {ClientIdentity}",
                    clientIdentity);
                return;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "ThreatDetection.Service unavailable, updating in-process for {ClientIdentity}",
                clientIdentity);
        }

        // Fallback: in-process update via DbContext
        using var scope = serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<SentinelGateDbContext>();

        var threatScore = await dbContext.ThreatScores
            .FirstOrDefaultAsync(t => t.ClientIdentity == clientIdentity);

        if (threatScore == null)
        {
            threatScore = new ThreatScore
            {
                Id = Guid.NewGuid(),
                ClientIdentity = clientIdentity,
                IpAddress = clientIp,
                Score = 0,
                LastUpdated = DateTime.UtcNow,
                LastDecayed = DateTime.UtcNow
            };
            dbContext.ThreatScores.Add(threatScore);
        }

        // Apply signal weights
        foreach (var (signal, weight) in signals)
        {
            switch (signal)
            {
                case "RateLimitViolation":
                    threatScore.RateLimitViolations++;
                    break;
                case "High4xxRate":
                    threatScore.High4xxRate += 1.0;
                    break;
                case "AuthFailure":
                    threatScore.AuthFailures++;
                    break;
            }

            threatScore.Score = Math.Min(100.0, threatScore.Score + weight);
        }

        threatScore.LastUpdated = DateTime.UtcNow;

        await dbContext.SaveChangesAsync();

        _logger.LogInformation(
            "Threat score updated in-process for {ClientIdentity}: {Score:F1}",
            clientIdentity, threatScore.Score);
    }
}
