using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SentinelGate.Shared.Infrastructure.Data;
using SentinelGate.Shared.Models.Configuration;
using SentinelGate.Shared.Models.Enums;
using SentinelGate.ThreatDetection.Service.Models;
using SentinelGate.ThreatDetection.Service.Services;
using SentinelGate.Tests.Helpers;

namespace SentinelGate.Tests.ThreatDetection;

public class ThreatScoringEngineTests : IDisposable
{
    private readonly TestFixture _fixture;
    private readonly ThreatScoringEngine _engine;

    public ThreatScoringEngineTests()
    {
        _fixture = new TestFixture();
        var logger = _fixture.ServiceProvider.GetRequiredService<ILogger<ThreatScoringEngine>>();
        var options = _fixture.ServiceProvider.GetRequiredService<IOptions<ThreatDetectionOptions>>();
        _engine = new ThreatScoringEngine(_fixture.ScopeFactory, options, logger);
    }

    [Fact]
    public async Task Test_InitialScoreIsZero()
    {
        // Arrange
        var clientId = $"client-{Guid.NewGuid():N}";

        // Act
        var result = await _engine.GetScore(clientId);

        // Assert - no score exists yet
        Assert.Null(result);
    }

    [Fact]
    public async Task Test_RateLimitViolationAdds15()
    {
        // Arrange
        var clientId = $"client-{Guid.NewGuid():N}";

        // Act
        var result = await _engine.UpdateScore(clientId, "10.0.0.1", ThreatSignal.RateLimitViolation);

        // Assert
        Assert.Equal(15.0, result.Score);
        Assert.Equal(clientId, result.ClientIdentity);
        Assert.Equal(ThreatAction.Allow, result.Action); // 15 < 31
        Assert.Contains(result.Triggers, t => t.Contains("RateLimitViolation"));
    }

    [Fact]
    public async Task Test_AuthFailureAdds25()
    {
        // Arrange
        var clientId = $"client-{Guid.NewGuid():N}";

        // Act
        var result = await _engine.UpdateScore(clientId, "10.0.0.1", ThreatSignal.AuthFailure);

        // Assert
        Assert.Equal(25.0, result.Score);
        Assert.Equal(ThreatAction.Allow, result.Action); // 25 < 31
    }

    [Fact]
    public async Task Test_ScoreCapsAt100()
    {
        // Arrange
        var clientId = $"client-{Guid.NewGuid():N}";

        // Act - send many signals to push score well beyond 100
        for (int i = 0; i < 10; i++)
        {
            await _engine.UpdateScore(clientId, "10.0.0.1", ThreatSignal.AuthFailure); // +25 each
        }

        var result = await _engine.GetScore(clientId);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(100.0, result.Score);
    }

    [Fact]
    public async Task Test_AutoBlocksAt90()
    {
        // Arrange
        var clientId = $"client-{Guid.NewGuid():N}";

        // Act - push score to >= 90 (4 x AuthFailure = 100, capped at 100)
        for (int i = 0; i < 4; i++)
        {
            await _engine.UpdateScore(clientId, "10.0.0.1", ThreatSignal.AuthFailure); // +25 each = 100
        }

        // Assert - client should be auto-blocked
        var blockedClient = await _fixture.DbContext.BlockedClients
            .FirstOrDefaultAsync(b => b.ClientIdentity == clientId && b.IsActive);

        Assert.NotNull(blockedClient);
        Assert.Contains("Auto-blocked", blockedClient.Reason);
        Assert.Equal(BlockType.Auto, blockedClient.BlockType);
    }

    [Fact]
    public async Task Test_DecayReducesScore()
    {
        // Arrange
        var clientId = $"client-{Guid.NewGuid():N}";
        await _engine.UpdateScore(clientId, "10.0.0.1", ThreatSignal.AuthFailure); // Score = 25

        // Simulate time passage by modifying LastDecayed in the database
        var threatScore = await _fixture.DbContext.ThreatScores
            .FirstAsync(t => t.ClientIdentity == clientId);
        threatScore.LastDecayed = DateTime.UtcNow.AddHours(-24); // 1 half-life ago
        await _fixture.DbContext.SaveChangesAsync();

        // Act
        await _engine.DecayScores();

        // Assert
        var result = await _engine.GetScore(clientId);
        Assert.NotNull(result);
        // After 1 half-life, score should be approximately halved: 25 * 0.5 = 12.5
        Assert.InRange(result.Score, 10.0, 15.0);
    }

    [Fact]
    public async Task Test_ResetScoreToZero()
    {
        // Arrange
        var clientId = $"client-{Guid.NewGuid():N}";
        await _engine.UpdateScore(clientId, "10.0.0.1", ThreatSignal.AuthFailure);
        await _engine.UpdateScore(clientId, "10.0.0.1", ThreatSignal.RateLimitViolation);

        var before = await _engine.GetScore(clientId);
        Assert.NotNull(before);
        Assert.True(before.Score > 0);

        // Act
        await _engine.ResetScore(clientId);

        // Assert
        var after = await _engine.GetScore(clientId);
        Assert.NotNull(after);
        Assert.Equal(0.0, after.Score);
        Assert.Equal(ThreatAction.Allow, after.Action);
    }

    [Theory]
    [InlineData(0, ThreatAction.Allow)]
    [InlineData(15, ThreatAction.Allow)]
    [InlineData(30, ThreatAction.Allow)]
    [InlineData(31, ThreatAction.Captcha)]
    [InlineData(50, ThreatAction.Captcha)]
    [InlineData(60, ThreatAction.Throttle)]
    [InlineData(75, ThreatAction.Throttle)]
    [InlineData(80, ThreatAction.TemporaryBlock)]
    [InlineData(89, ThreatAction.TemporaryBlock)]
    [InlineData(90, ThreatAction.PermanentBlock)]
    [InlineData(100, ThreatAction.PermanentBlock)]
    public async Task Test_CorrectActionForScoreRange(double targetScore, ThreatAction expectedAction)
    {
        // Arrange - create a score entity directly for precise control
        var clientId = $"client-{Guid.NewGuid():N}";
        _fixture.DbContext.ThreatScores.Add(new Shared.Models.Entities.ThreatScore
        {
            Id = Guid.NewGuid(),
            ClientIdentity = clientId,
            IpAddress = "10.0.0.1",
            Score = targetScore,
            LastUpdated = DateTime.UtcNow,
            LastDecayed = DateTime.UtcNow
        });
        await _fixture.DbContext.SaveChangesAsync();

        // Act
        var result = await _engine.GetScore(clientId);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(expectedAction, result.Action);
    }

    [Fact]
    public async Task Test_MultipleSignalsAccumulate()
    {
        // Arrange
        var clientId = $"client-{Guid.NewGuid():N}";

        // Act
        await _engine.UpdateScore(clientId, "10.0.0.1", ThreatSignal.RateLimitViolation); // +15
        var result = await _engine.UpdateScore(clientId, "10.0.0.1", ThreatSignal.AuthFailure); // +25

        // Assert
        Assert.Equal(40.0, result.Score); // 15 + 25
        Assert.Equal(ThreatAction.Captcha, result.Action); // 40 >= 31
    }

    public void Dispose()
    {
        _fixture.Dispose();
        GC.SuppressFinalize(this);
    }
}
