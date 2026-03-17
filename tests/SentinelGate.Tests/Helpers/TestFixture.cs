using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SentinelGate.Shared.Infrastructure.Data;
using SentinelGate.Shared.Infrastructure.Redis;
using SentinelGate.Shared.Models.Configuration;

namespace SentinelGate.Tests.Helpers;

/// <summary>
/// Shared test infrastructure that provides an in-memory database,
/// a RedisConnectionManager that cannot connect (triggers in-memory fallback),
/// and pre-configured options for all services.
/// </summary>
public class TestFixture : IDisposable
{
    public SentinelGateDbContext DbContext { get; }
    public IServiceProvider ServiceProvider { get; }
    public IServiceScopeFactory ScopeFactory { get; }
    public RedisConnectionManager RedisManager { get; }

    private readonly ServiceProvider _rootProvider;

    public TestFixture()
    {
        var services = new ServiceCollection();

        // In-memory EF Core database with a unique name per fixture instance
        var dbName = $"SentinelGateTest_{Guid.NewGuid():N}";
        services.AddDbContext<SentinelGateDbContext>(options =>
            options.UseInMemoryDatabase(dbName));

        // Logging
        services.AddLogging(builder => builder.AddDebug());

        // SentinelGate options with default threat detection weights
        services.Configure<SentinelGateOptions>(opts =>
        {
            opts.RateLimiting = new RateLimitingOptions
            {
                DefaultAlgorithm = Shared.Models.Enums.RateLimitAlgorithm.SlidingWindow,
                DefaultLimit = 100,
                DefaultWindowSeconds = 60,
                DefaultBurstLimit = 20,
                DefaultRefillRate = 10.0
            };
            opts.ThreatDetection = CreateDefaultThreatOptions();
        });

        services.Configure<ThreatDetectionOptions>(opts =>
        {
            var defaults = CreateDefaultThreatOptions();
            opts.Enabled = defaults.Enabled;
            opts.AutoBlockThreshold = defaults.AutoBlockThreshold;
            opts.DecayHalfLifeHours = defaults.DecayHalfLifeHours;
            opts.RateLimitViolationWeight = defaults.RateLimitViolationWeight;
            opts.High4xxRateWeight = defaults.High4xxRateWeight;
            opts.AuthFailureWeight = defaults.AuthFailureWeight;
            opts.SingleEndpointHammeringWeight = defaults.SingleEndpointHammeringWeight;
            opts.UserAgentAnomalyWeight = defaults.UserAgentAnomalyWeight;
            opts.GeoMismatchWeight = defaults.GeoMismatchWeight;
            opts.PayloadAnomalyWeight = defaults.PayloadAnomalyWeight;
        });

        _rootProvider = services.BuildServiceProvider();
        ServiceProvider = _rootProvider;
        ScopeFactory = _rootProvider.GetRequiredService<IServiceScopeFactory>();
        DbContext = _rootProvider.GetRequiredService<SentinelGateDbContext>();

        // RedisConnectionManager with an unreachable address.
        // TryConnect will fail silently, GetDatabase() will return null,
        // causing all rate limiters to use their in-memory fallback paths.
        RedisManager = new RedisConnectionManager("localhost:0,abortConnect=false,connectTimeout=1");
    }

    public static ThreatDetectionOptions CreateDefaultThreatOptions() => new()
    {
        Enabled = true,
        AutoBlockThreshold = 90.0,
        DecayHalfLifeHours = 24.0,
        RateLimitViolationWeight = 15,
        High4xxRateWeight = 20,
        AuthFailureWeight = 25,
        SingleEndpointHammeringWeight = 20,
        UserAgentAnomalyWeight = 10,
        GeoMismatchWeight = 10,
        PayloadAnomalyWeight = 15
    };

    /// <summary>
    /// Creates a fresh DbContext instance sharing the same in-memory database.
    /// Useful when testing services that create their own scopes.
    /// </summary>
    public SentinelGateDbContext CreateDbContext()
    {
        return ServiceProvider.GetRequiredService<SentinelGateDbContext>();
    }

    public void Dispose()
    {
        DbContext.Database.EnsureDeleted();
        RedisManager.Dispose();
        _rootProvider.Dispose();
        GC.SuppressFinalize(this);
    }
}
