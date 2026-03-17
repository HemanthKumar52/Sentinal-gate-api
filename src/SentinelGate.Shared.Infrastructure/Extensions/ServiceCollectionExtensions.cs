using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SentinelGate.Shared.Infrastructure.Data;
using SentinelGate.Shared.Infrastructure.Redis;
using SentinelGate.Shared.Infrastructure.Services;

namespace SentinelGate.Shared.Infrastructure.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddSentinelGateDbContext(
        this IServiceCollection services,
        string connectionString,
        bool useInMemory = false)
    {
        if (useInMemory)
        {
            services.AddDbContext<SentinelGateDbContext>(options =>
                options.UseInMemoryDatabase("SentinelGate"));
        }
        else
        {
            services.AddDbContext<SentinelGateDbContext>(options =>
                options.UseNpgsql(connectionString, npgsql =>
                {
                    npgsql.EnableRetryOnFailure(
                        maxRetryCount: 3,
                        maxRetryDelay: TimeSpan.FromSeconds(10),
                        errorCodesToAdd: null);
                }));
        }

        return services;
    }

    public static IServiceCollection AddSentinelGateRedis(
        this IServiceCollection services,
        string connectionString)
    {
        services.AddSingleton(new RedisConnectionManager(connectionString));
        services.AddSingleton<RateLimitCounter>();

        return services;
    }

    public static IServiceCollection AddTelemetryChannel(
        this IServiceCollection services,
        int maxBuffer = 10_000)
    {
        services.AddSingleton(new TelemetryChannel(maxBuffer));

        return services;
    }
}
