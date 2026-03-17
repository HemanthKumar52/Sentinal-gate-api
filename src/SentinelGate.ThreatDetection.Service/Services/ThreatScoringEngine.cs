using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SentinelGate.Shared.Infrastructure.Data;
using SentinelGate.Shared.Models.Configuration;
using SentinelGate.Shared.Models.DTOs;
using SentinelGate.Shared.Models.Entities;
using SentinelGate.Shared.Models.Enums;
using SentinelGate.ThreatDetection.Service.Models;

namespace SentinelGate.ThreatDetection.Service.Services;

/// <summary>
/// Core threat scoring engine. Computes cumulative threat scores per client identity,
/// determines appropriate action thresholds, and auto-blocks clients that exceed
/// the configured permanent block threshold.
/// </summary>
public class ThreatScoringEngine
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ThreatDetectionOptions _options;
    private readonly ILogger<ThreatScoringEngine> _logger;

    public ThreatScoringEngine(
        IServiceScopeFactory scopeFactory,
        IOptions<ThreatDetectionOptions> options,
        ILogger<ThreatScoringEngine> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options.Value;
        _logger = logger;
    }

    /// <summary>
    /// Updates the threat score for a given client based on the received signal.
    /// </summary>
    public async Task<ThreatScoreResult> UpdateScore(string clientIdentity, string ipAddress, ThreatSignal signal)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SentinelGateDbContext>();

        var threatScore = await db.ThreatScores
            .FirstOrDefaultAsync(t => t.ClientIdentity == clientIdentity);

        if (threatScore == null)
        {
            threatScore = new ThreatScore
            {
                Id = Guid.NewGuid(),
                ClientIdentity = clientIdentity,
                IpAddress = ipAddress,
                Score = 0,
                LastUpdated = DateTime.UtcNow,
                LastDecayed = DateTime.UtcNow
            };
            db.ThreatScores.Add(threatScore);
        }

        // Update IP if changed
        threatScore.IpAddress = ipAddress;

        // Apply signal weight
        var weight = GetSignalWeight(signal);
        IncrementSignalCounter(threatScore, signal);

        // Add weight, clamp to 100
        threatScore.Score = Math.Min(100.0, threatScore.Score + weight);
        threatScore.LastUpdated = DateTime.UtcNow;

        // Determine action
        var action = DetermineAction(threatScore.Score);
        var triggers = BuildTriggerList(threatScore);

        // Auto-block if score crosses threshold
        if (threatScore.Score >= _options.AutoBlockThreshold)
        {
            await AutoBlockClient(db, threatScore, action);
        }

        await db.SaveChangesAsync();

        _logger.LogInformation(
            "Threat score updated for {Client}: {Score:F1} ({Action}) - signal: {Signal} (+{Weight})",
            clientIdentity, threatScore.Score, action, signal, weight);

        return new ThreatScoreResult(clientIdentity, threatScore.Score, action, triggers);
    }

    /// <summary>
    /// Gets the current threat score for a client.
    /// </summary>
    public async Task<ThreatScoreResult?> GetScore(string clientIdentity)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SentinelGateDbContext>();

        var threatScore = await db.ThreatScores
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.ClientIdentity == clientIdentity);

        if (threatScore == null)
            return null;

        var action = DetermineAction(threatScore.Score);
        var triggers = BuildTriggerList(threatScore);

        return new ThreatScoreResult(clientIdentity, threatScore.Score, action, triggers);
    }

    /// <summary>
    /// Decays all threat scores using exponential decay based on a configurable half-life.
    /// Formula: score * 2^(-elapsed_hours / half_life_hours)
    /// </summary>
    public async Task DecayScores()
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SentinelGateDbContext>();

        var now = DateTime.UtcNow;
        var scores = await db.ThreatScores.Where(t => t.Score > 0).ToListAsync();

        var halfLife = _options.DecayHalfLifeHours;
        var decayedCount = 0;

        foreach (var score in scores)
        {
            var hoursElapsed = (now - score.LastDecayed).TotalHours;
            if (hoursElapsed <= 0) continue;

            var decayFactor = Math.Pow(2.0, -hoursElapsed / halfLife);
            var newScore = score.Score * decayFactor;

            // If score drops below 0.5, just zero it out
            if (newScore < 0.5)
            {
                newScore = 0;
                ResetSignalCounters(score);
            }

            score.Score = newScore;
            score.LastDecayed = now;
            decayedCount++;
        }

        await db.SaveChangesAsync();

        _logger.LogInformation(
            "Decay cycle complete: {Count} scores decayed (half-life: {HalfLife}h)",
            decayedCount, halfLife);
    }

    /// <summary>
    /// Resets a client's threat score to zero and clears all signal counters.
    /// </summary>
    public async Task ResetScore(string clientIdentity)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SentinelGateDbContext>();

        var threatScore = await db.ThreatScores
            .FirstOrDefaultAsync(t => t.ClientIdentity == clientIdentity);

        if (threatScore != null)
        {
            threatScore.Score = 0;
            ResetSignalCounters(threatScore);
            threatScore.LastUpdated = DateTime.UtcNow;
            threatScore.LastDecayed = DateTime.UtcNow;
            await db.SaveChangesAsync();

            _logger.LogInformation("Threat score reset for {Client}", clientIdentity);
        }
    }

    // ─── Private helpers ──────────────────────────────────────────────────────

    private int GetSignalWeight(ThreatSignal signal) => signal switch
    {
        ThreatSignal.RateLimitViolation => _options.RateLimitViolationWeight,
        ThreatSignal.High4xxRate => _options.High4xxRateWeight,
        ThreatSignal.AuthFailure => _options.AuthFailureWeight,
        ThreatSignal.SingleEndpointHammering => _options.SingleEndpointHammeringWeight,
        ThreatSignal.UserAgentAnomaly => _options.UserAgentAnomalyWeight,
        ThreatSignal.GeoMismatch => _options.GeoMismatchWeight,
        ThreatSignal.PayloadAnomaly => _options.PayloadAnomalyWeight,
        _ => 0
    };

    private static void IncrementSignalCounter(ThreatScore score, ThreatSignal signal)
    {
        switch (signal)
        {
            case ThreatSignal.RateLimitViolation:
                score.RateLimitViolations++;
                break;
            case ThreatSignal.High4xxRate:
                score.High4xxRate++;
                break;
            case ThreatSignal.AuthFailure:
                score.AuthFailures++;
                break;
            case ThreatSignal.SingleEndpointHammering:
                score.SingleEndpointHammering++;
                break;
            case ThreatSignal.UserAgentAnomaly:
                score.UserAgentAnomaly++;
                break;
            case ThreatSignal.GeoMismatch:
                score.GeoMismatch++;
                break;
            case ThreatSignal.PayloadAnomaly:
                score.PayloadAnomaly++;
                break;
        }
    }

    private static void ResetSignalCounters(ThreatScore score)
    {
        score.RateLimitViolations = 0;
        score.High4xxRate = 0;
        score.AuthFailures = 0;
        score.SingleEndpointHammering = 0;
        score.UserAgentAnomaly = 0;
        score.GeoMismatch = 0;
        score.PayloadAnomaly = 0;
    }

    private ThreatAction DetermineAction(double score) => score switch
    {
        >= 90.0 => ThreatAction.PermanentBlock,
        >= 80.0 => ThreatAction.TemporaryBlock,
        >= 60.0 => ThreatAction.Throttle,
        >= 31.0 => ThreatAction.Captcha,
        _ => ThreatAction.Allow
    };

    private static List<string> BuildTriggerList(ThreatScore score)
    {
        var triggers = new List<string>();
        if (score.RateLimitViolations > 0) triggers.Add($"RateLimitViolation (x{score.RateLimitViolations})");
        if (score.High4xxRate > 0) triggers.Add($"High4xxRate (x{(int)score.High4xxRate})");
        if (score.AuthFailures > 0) triggers.Add($"AuthFailure (x{score.AuthFailures})");
        if (score.SingleEndpointHammering > 0) triggers.Add($"SingleEndpointHammering (x{(int)score.SingleEndpointHammering})");
        if (score.UserAgentAnomaly > 0) triggers.Add($"UserAgentAnomaly (x{(int)score.UserAgentAnomaly})");
        if (score.GeoMismatch > 0) triggers.Add($"GeoMismatch (x{(int)score.GeoMismatch})");
        if (score.PayloadAnomaly > 0) triggers.Add($"PayloadAnomaly (x{(int)score.PayloadAnomaly})");
        return triggers;
    }

    private async Task AutoBlockClient(SentinelGateDbContext db, ThreatScore threatScore, ThreatAction action)
    {
        var existingBlock = await db.BlockedClients
            .FirstOrDefaultAsync(b =>
                b.ClientIdentity == threatScore.ClientIdentity &&
                b.IsActive &&
                !b.IsDeleted);

        if (existingBlock != null) return;

        var blockType = action == ThreatAction.PermanentBlock
            ? BlockType.Auto
            : BlockType.Auto;

        var blockedClient = new BlockedClient
        {
            Id = Guid.NewGuid(),
            ClientIdentity = threatScore.ClientIdentity,
            IpAddress = threatScore.IpAddress,
            Reason = $"Auto-blocked: threat score {threatScore.Score:F1} exceeded threshold {_options.AutoBlockThreshold}",
            BlockType = blockType,
            ThreatScore = threatScore.Score,
            ExpiresAt = action == ThreatAction.TemporaryBlock
                ? DateTime.UtcNow.AddHours(24)
                : null,
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "ThreatScoringEngine"
        };

        db.BlockedClients.Add(blockedClient);

        _logger.LogWarning(
            "Auto-blocked client {Client} (IP: {Ip}) - score: {Score:F1}, action: {Action}",
            threatScore.ClientIdentity, threatScore.IpAddress, threatScore.Score, action);
    }
}
