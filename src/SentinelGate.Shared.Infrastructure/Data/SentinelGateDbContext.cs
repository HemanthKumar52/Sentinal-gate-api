using Microsoft.EntityFrameworkCore;
using SentinelGate.Shared.Models.Entities;

namespace SentinelGate.Shared.Infrastructure.Data;

public class SentinelGateDbContext : DbContext
{
    public SentinelGateDbContext(DbContextOptions<SentinelGateDbContext> options)
        : base(options)
    {
    }

    public DbSet<RequestLog> RequestLogs => Set<RequestLog>();
    public DbSet<RatePolicy> RatePolicies => Set<RatePolicy>();
    public DbSet<BlockedClient> BlockedClients => Set<BlockedClient>();
    public DbSet<ThreatScore> ThreatScores => Set<ThreatScore>();
    public DbSet<ApiKeyEntity> ApiKeys => Set<ApiKeyEntity>();
    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<WebhookSubscription> WebhookSubscriptions => Set<WebhookSubscription>();
    public DbSet<AlertEvent> AlertEvents => Set<AlertEvent>();
    public DbSet<HourlyAggregate> HourlyAggregates => Set<HourlyAggregate>();
    public DbSet<DailyAggregate> DailyAggregates => Set<DailyAggregate>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // RequestLog indexes
        modelBuilder.Entity<RequestLog>(entity =>
        {
            entity.HasIndex(e => e.ClientIdentity);
            entity.HasIndex(e => e.ClientIp);
            entity.HasIndex(e => e.Timestamp);
            entity.HasIndex(e => e.EndpointPath);
            entity.HasIndex(e => new { e.Timestamp, e.EndpointPath })
                  .HasDatabaseName("IX_RequestLogs_Timestamp_EndpointPath");
            entity.HasIndex(e => new { e.ClientIdentity, e.Timestamp })
                  .HasDatabaseName("IX_RequestLogs_ClientIdentity_Timestamp");
        });

        // RatePolicy configuration
        modelBuilder.Entity<RatePolicy>(entity =>
        {
            entity.HasIndex(e => e.IsGlobal);
            entity.HasIndex(e => e.TenantId);
            entity.HasIndex(e => e.IsEnabled);
        });

        // BlockedClient indexes
        modelBuilder.Entity<BlockedClient>(entity =>
        {
            entity.HasIndex(e => e.ClientIdentity);
            entity.HasIndex(e => e.IpAddress);
            entity.HasIndex(e => e.IsActive);
            entity.HasIndex(e => new { e.IsActive, e.ExpiresAt })
                  .HasDatabaseName("IX_BlockedClients_IsActive_ExpiresAt");
        });

        // ThreatScore indexes
        modelBuilder.Entity<ThreatScore>(entity =>
        {
            entity.HasIndex(e => e.ClientIdentity).IsUnique();
            entity.HasIndex(e => e.IpAddress);
        });

        // ApiKeyEntity indexes
        modelBuilder.Entity<ApiKeyEntity>(entity =>
        {
            entity.HasIndex(e => e.HashedKey).IsUnique();
            entity.HasIndex(e => e.TenantId);
            entity.HasIndex(e => e.IsActive);
        });

        // Tenant indexes
        modelBuilder.Entity<Tenant>(entity =>
        {
            entity.HasIndex(e => e.Name).IsUnique();
            entity.HasIndex(e => e.IsActive);
        });

        // AuditLog indexes
        modelBuilder.Entity<AuditLog>(entity =>
        {
            entity.HasIndex(e => e.Timestamp);
            entity.HasIndex(e => e.Actor);
            entity.HasIndex(e => e.IpAddress);
        });

        // WebhookSubscription indexes
        modelBuilder.Entity<WebhookSubscription>(entity =>
        {
            entity.HasIndex(e => e.TenantId);
            entity.HasIndex(e => e.IsActive);
        });

        // AlertEvent indexes
        modelBuilder.Entity<AlertEvent>(entity =>
        {
            entity.HasIndex(e => e.ClientIdentity);
            entity.HasIndex(e => e.CreatedAt);
            entity.HasIndex(e => e.Severity);
            entity.HasIndex(e => e.IsAcknowledged);
        });

        // HourlyAggregate indexes
        modelBuilder.Entity<HourlyAggregate>(entity =>
        {
            entity.HasIndex(e => e.EndpointPath);
            entity.HasIndex(e => e.Hour);
            entity.HasIndex(e => new { e.EndpointPath, e.Hour })
                  .IsUnique()
                  .HasDatabaseName("IX_HourlyAggregates_EndpointPath_Hour");
        });

        // DailyAggregate indexes
        modelBuilder.Entity<DailyAggregate>(entity =>
        {
            entity.HasIndex(e => e.EndpointPath);
            entity.HasIndex(e => e.Date);
            entity.HasIndex(e => new { e.EndpointPath, e.Date })
                  .IsUnique()
                  .HasDatabaseName("IX_DailyAggregates_EndpointPath_Date");
        });
    }
}
