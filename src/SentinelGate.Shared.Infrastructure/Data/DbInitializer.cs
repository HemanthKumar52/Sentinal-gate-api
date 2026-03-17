using Microsoft.EntityFrameworkCore;
using SentinelGate.Shared.Models.Entities;
using SentinelGate.Shared.Models.Enums;

namespace SentinelGate.Shared.Infrastructure.Data;

public static class DbInitializer
{
    public static async Task SeedAsync(SentinelGateDbContext context)
    {
        await context.Database.EnsureCreatedAsync();

        // Seed default Global rate policy if none exists
        if (!await context.RatePolicies.AnyAsync(p => p.IsGlobal))
        {
            context.RatePolicies.Add(new RatePolicy
            {
                Id = Guid.NewGuid(),
                Name = "Global Default",
                Algorithm = RateLimitAlgorithm.SlidingWindow,
                Limit = 100,
                WindowSeconds = 60,
                IsGlobal = true,
                IsEnabled = true,
                Priority = 0,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
        }

        // Seed default tenants for each tier if none exist
        if (!await context.Tenants.AnyAsync())
        {
            context.Tenants.AddRange(
                new Tenant
                {
                    Id = Guid.NewGuid(),
                    Name = "Free Tier",
                    Tier = TenantTier.Free,
                    DailyQuota = 10_000,
                    MonthlyQuota = 100_000,
                    IsActive = true,
                    CreatedAt = DateTime.UtcNow
                },
                new Tenant
                {
                    Id = Guid.NewGuid(),
                    Name = "Pro Tier",
                    Tier = TenantTier.Pro,
                    DailyQuota = 100_000,
                    MonthlyQuota = 2_000_000,
                    IsActive = true,
                    CreatedAt = DateTime.UtcNow
                },
                new Tenant
                {
                    Id = Guid.NewGuid(),
                    Name = "Enterprise Tier",
                    Tier = TenantTier.Enterprise,
                    DailyQuota = 1_000_000,
                    MonthlyQuota = 50_000_000,
                    IsActive = true,
                    CreatedAt = DateTime.UtcNow
                }
            );
        }

        await context.SaveChangesAsync();
    }
}
