using Microsoft.EntityFrameworkCore;
using SentinelGate.Identity.Service.Services;
using SentinelGate.Shared.Infrastructure.Data;
using SentinelGate.Shared.Models.DTOs;
using SentinelGate.Tests.Helpers;

namespace SentinelGate.Tests.Identity;

public class ApiKeyServiceTests : IDisposable
{
    private readonly TestFixture _fixture;
    private readonly ApiKeyService _service;
    private readonly SentinelGateDbContext _db;

    public ApiKeyServiceTests()
    {
        _fixture = new TestFixture();
        _db = _fixture.DbContext;
        _service = new ApiKeyService(_db);
    }

    private static CreateApiKeyRequest MakeRequest(string name = "Test Key", string tenantId = "tenant-1")
    {
        return new CreateApiKeyRequest(
            Name: name,
            TenantId: tenantId,
            UserId: "user-1",
            Scopes: "read,write",
            RateLimitOverride: null,
            ExpiresAt: null
        );
    }

    [Fact]
    public async Task Test_GenerateKey_CreatesValidKey()
    {
        // Arrange
        var request = MakeRequest();

        // Act
        var result = await _service.GenerateKey(request);

        // Assert
        Assert.NotNull(result);
        Assert.NotEmpty(result.Key);
        Assert.Equal(32, result.Key.Length); // Full raw key is 32 chars
        Assert.Equal("Test Key", result.Name);
        Assert.True(result.IsActive);
        Assert.NotEqual(Guid.Empty, result.Id);

        // Verify it was persisted
        var entity = await _db.ApiKeys.FindAsync(result.Id);
        Assert.NotNull(entity);
        Assert.True(entity.IsActive);
        Assert.NotEmpty(entity.HashedKey);
    }

    [Fact]
    public async Task Test_ValidateKey_AcceptsValidKey()
    {
        // Arrange
        var request = MakeRequest();
        var generated = await _service.GenerateKey(request);
        var rawKey = generated.Key;

        // Act
        var entity = await _service.ValidateKey(rawKey);

        // Assert
        Assert.NotNull(entity);
        Assert.Equal(generated.Id, entity.Id);
        Assert.True(entity.IsActive);
    }

    [Fact]
    public async Task Test_ValidateKey_RejectsRevokedKey()
    {
        // Arrange
        var request = MakeRequest();
        var generated = await _service.GenerateKey(request);
        var rawKey = generated.Key;

        // Revoke the key
        await _service.RevokeKey(generated.Id);

        // Act
        var entity = await _service.ValidateKey(rawKey);

        // Assert
        Assert.Null(entity);
    }

    [Fact]
    public async Task Test_RotateKey_CreatesNewKeyAndKeepsOld()
    {
        // Arrange
        var request = MakeRequest();
        var original = await _service.GenerateKey(request);

        // Act
        var rotated = await _service.RotateKey(original.Id);

        // Assert
        Assert.NotNull(rotated);
        Assert.NotEqual(original.Id, rotated.Id);
        Assert.NotEqual(original.Key, rotated.Key);
        Assert.Equal(original.Name, rotated.Name);
        Assert.True(rotated.IsActive);

        // Old key should still be active (grace period) but with an expiration
        var oldEntity = await _db.ApiKeys.FindAsync(original.Id);
        Assert.NotNull(oldEntity);
        Assert.True(oldEntity.IsActive);
        Assert.NotNull(oldEntity.ExpiresAt);

        // New key should reference the old one
        var newEntity = await _db.ApiKeys.FindAsync(rotated.Id);
        Assert.NotNull(newEntity);
        Assert.Equal(original.Id, newEntity.RotatedFromKeyId);
    }

    [Fact]
    public async Task Test_RevokeKey_ImmediatelyInvalidates()
    {
        // Arrange
        var request = MakeRequest();
        var generated = await _service.GenerateKey(request);

        // Act
        var revoked = await _service.RevokeKey(generated.Id);

        // Assert
        Assert.True(revoked);

        var entity = await _db.ApiKeys.FindAsync(generated.Id);
        Assert.NotNull(entity);
        Assert.False(entity.IsActive);
        Assert.NotNull(entity.RevokedAt);

        // Validation should now fail
        var validated = await _service.ValidateKey(generated.Key);
        Assert.Null(validated);
    }

    [Fact]
    public async Task Test_RevokeKey_ReturnsFalseForNonexistent()
    {
        // Act
        var result = await _service.RevokeKey(Guid.NewGuid());

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task Test_ValidateKey_RejectsExpiredKey()
    {
        // Arrange
        var request = new CreateApiKeyRequest(
            Name: "Expiring Key",
            TenantId: "tenant-1",
            UserId: "user-1",
            Scopes: "read",
            RateLimitOverride: null,
            ExpiresAt: DateTime.UtcNow.AddSeconds(-1) // Already expired
        );
        var generated = await _service.GenerateKey(request);

        // Act
        var entity = await _service.ValidateKey(generated.Key);

        // Assert
        Assert.Null(entity);
    }

    [Fact]
    public async Task Test_RotateKey_ReturnsNullForInactiveKey()
    {
        // Arrange
        var request = MakeRequest();
        var generated = await _service.GenerateKey(request);
        await _service.RevokeKey(generated.Id);

        // Act
        var rotated = await _service.RotateKey(generated.Id);

        // Assert
        Assert.Null(rotated);
    }

    [Fact]
    public async Task Test_GetKeys_ReturnsAllKeysForTenant()
    {
        // Arrange
        var tenantId = $"tenant-{Guid.NewGuid():N}";
        await _service.GenerateKey(MakeRequest("Key 1", tenantId));
        await _service.GenerateKey(MakeRequest("Key 2", tenantId));
        await _service.GenerateKey(MakeRequest("Key 3", "other-tenant"));

        // Act
        var keys = await _service.GetKeys(tenantId);

        // Assert
        Assert.Equal(2, keys.Count);
        Assert.All(keys, k => Assert.Contains("...", k.Key)); // Keys should be masked
    }

    public void Dispose()
    {
        _fixture.Dispose();
        GC.SuppressFinalize(this);
    }
}
