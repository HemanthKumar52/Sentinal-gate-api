using Microsoft.EntityFrameworkCore;
using SentinelGate.Shared.Infrastructure.Data;
using SentinelGate.Shared.Models.DTOs;
using SentinelGate.Shared.Models.Entities;
using SentinelGate.Shared.Models.Enums;

namespace SentinelGate.Identity.Service.Services;

public class TenantService
{
    private readonly SentinelGateDbContext _db;

    public TenantService(SentinelGateDbContext db)
    {
        _db = db;
    }

    public async Task<Tenant> CreateTenant(string name, TenantTier tier)
    {
        var (dailyQuota, monthlyQuota) = GetQuotasForTier(tier);

        var tenant = new Tenant
        {
            Id = Guid.NewGuid(),
            Name = name,
            Tier = tier,
            DailyQuota = dailyQuota,
            MonthlyQuota = monthlyQuota,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };

        _db.Tenants.Add(tenant);
        await _db.SaveChangesAsync();
        return tenant;
    }

    public async Task<Tenant?> GetTenant(Guid id)
    {
        return await _db.Tenants.FindAsync(id);
    }

    public async Task<List<Tenant>> GetAllTenants()
    {
        return await _db.Tenants
            .OrderByDescending(t => t.CreatedAt)
            .ToListAsync();
    }

    public async Task<bool> UpdateTenantTier(Guid id, TenantTier tier)
    {
        var tenant = await _db.Tenants.FindAsync(id);
        if (tenant == null)
            return false;

        var (dailyQuota, monthlyQuota) = GetQuotasForTier(tier);
        tenant.Tier = tier;
        tenant.DailyQuota = dailyQuota;
        tenant.MonthlyQuota = monthlyQuota;

        await _db.SaveChangesAsync();
        return true;
    }

    public async Task<ClientUsageDto> GetTenantUsage(Guid id)
    {
        var tenant = await _db.Tenants.FindAsync(id);
        if (tenant == null)
            return new ClientUsageDto("unknown", 0, 0, 0, 0);

        var today = DateTime.UtcNow.Date;
        var logs = await _db.RequestLogs
            .Where(r => r.TenantId == id.ToString() && r.Timestamp >= today)
            .ToListAsync();

        var totalRequests = logs.Count;
        var errorCount = logs.Count(r => r.ResponseStatusCode >= 400);
        var errorRate = totalRequests > 0 ? (double)errorCount / totalRequests * 100 : 0;

        return new ClientUsageDto(
            tenant.Name,
            totalRequests,
            totalRequests,
            tenant.DailyQuota,
            Math.Round(errorRate, 2)
        );
    }

    private static (long dailyQuota, long monthlyQuota) GetQuotasForTier(TenantTier tier)
    {
        return tier switch
        {
            TenantTier.Free => (1_000, 30_000),
            TenantTier.Pro => (50_000, 1_500_000),
            TenantTier.Enterprise => (1_000_000, 30_000_000),
            _ => (1_000, 30_000)
        };
    }
}
