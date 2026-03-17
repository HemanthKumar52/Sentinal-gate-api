using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using SentinelGate.Shared.Infrastructure.Data;
using SentinelGate.Shared.Models.DTOs;
using SentinelGate.Shared.Models.Entities;

namespace SentinelGate.Identity.Service.Services;

public class ApiKeyService
{
    private readonly SentinelGateDbContext _db;

    public ApiKeyService(SentinelGateDbContext db)
    {
        _db = db;
    }

    public async Task<ApiKeyDto> GenerateKey(CreateApiKeyRequest request)
    {
        var rawKey = GenerateRandomKey(32);
        var hashedKey = HashKey(rawKey);

        var entity = new ApiKeyEntity
        {
            Id = Guid.NewGuid(),
            Key = rawKey[..8] + "..." + rawKey[^4..],  // store masked prefix for display
            HashedKey = hashedKey,
            Name = request.Name,
            TenantId = request.TenantId,
            UserId = request.UserId,
            Scopes = request.Scopes,
            RateLimitOverride = request.RateLimitOverride,
            ExpiresAt = request.ExpiresAt,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };

        _db.ApiKeys.Add(entity);
        await _db.SaveChangesAsync();

        // Return the full raw key only on creation
        return new ApiKeyDto(
            entity.Id,
            entity.Name,
            rawKey,
            entity.Scopes,
            entity.ExpiresAt,
            entity.IsActive,
            entity.CreatedAt
        );
    }

    public async Task<ApiKeyDto?> RotateKey(Guid keyId)
    {
        var existing = await _db.ApiKeys.FindAsync(keyId);
        if (existing == null || !existing.IsActive)
            return null;

        // Mark old key as rotated, keep valid for 24h grace period
        existing.ExpiresAt = DateTime.UtcNow.AddHours(24);
        existing.IsActive = true; // still valid during grace

        // Generate new key
        var rawKey = GenerateRandomKey(32);
        var hashedKey = HashKey(rawKey);

        var newEntity = new ApiKeyEntity
        {
            Id = Guid.NewGuid(),
            Key = rawKey[..8] + "..." + rawKey[^4..],
            HashedKey = hashedKey,
            Name = existing.Name,
            TenantId = existing.TenantId,
            UserId = existing.UserId,
            Scopes = existing.Scopes,
            RateLimitOverride = existing.RateLimitOverride,
            RotatedFromKeyId = existing.Id,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };

        _db.ApiKeys.Add(newEntity);
        await _db.SaveChangesAsync();

        return new ApiKeyDto(
            newEntity.Id,
            newEntity.Name,
            rawKey,
            newEntity.Scopes,
            newEntity.ExpiresAt,
            newEntity.IsActive,
            newEntity.CreatedAt
        );
    }

    public async Task<bool> RevokeKey(Guid keyId)
    {
        var entity = await _db.ApiKeys.FindAsync(keyId);
        if (entity == null)
            return false;

        entity.IsActive = false;
        entity.RevokedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return true;
    }

    public async Task<ApiKeyEntity?> ValidateKey(string key)
    {
        var hashedKey = HashKey(key);
        var entity = await _db.ApiKeys
            .FirstOrDefaultAsync(k => k.HashedKey == hashedKey && k.IsActive);

        if (entity == null)
            return null;

        // Check expiration
        if (entity.ExpiresAt.HasValue && entity.ExpiresAt.Value < DateTime.UtcNow)
        {
            entity.IsActive = false;
            await _db.SaveChangesAsync();
            return null;
        }

        return entity;
    }

    public async Task<List<ApiKeyDto>> GetKeys(string tenantId)
    {
        return await _db.ApiKeys
            .Where(k => k.TenantId == tenantId)
            .OrderByDescending(k => k.CreatedAt)
            .Select(k => new ApiKeyDto(
                k.Id,
                k.Name,
                k.Key,  // masked key
                k.Scopes,
                k.ExpiresAt,
                k.IsActive,
                k.CreatedAt
            ))
            .ToListAsync();
    }

    public async Task<ApiKeyDto?> GetKeyById(Guid id)
    {
        var k = await _db.ApiKeys.FindAsync(id);
        if (k == null)
            return null;

        return new ApiKeyDto(
            k.Id,
            k.Name,
            k.Key,
            k.Scopes,
            k.ExpiresAt,
            k.IsActive,
            k.CreatedAt
        );
    }

    private static string GenerateRandomKey(int length)
    {
        const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";
        var data = RandomNumberGenerator.GetBytes(length);
        var result = new char[length];
        for (int i = 0; i < length; i++)
            result[i] = chars[data[i] % chars.Length];
        return new string(result);
    }

    private static string HashKey(string key)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(key));
        return Convert.ToBase64String(bytes);
    }
}
