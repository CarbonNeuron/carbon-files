using CarbonFiles.Core.Models;
using CarbonFiles.Core.Models.Requests;
using CarbonFiles.Infrastructure.Data;
using CarbonFiles.Infrastructure.Data.Entities;
using CarbonFiles.Infrastructure.Services;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CarbonFiles.Infrastructure.Tests.Services;

public class UploadTokenServiceTests : IDisposable
{
    private readonly CarbonFilesDbContext _db;
    private readonly UploadTokenService _sut;

    public UploadTokenServiceTests()
    {
        var dbOptions = new DbContextOptionsBuilder<CarbonFilesDbContext>()
            .UseSqlite("Data Source=:memory:")
            .Options;

        _db = new CarbonFilesDbContext(dbOptions);
        _db.Database.OpenConnection();
        _db.Database.EnsureCreated();

        _sut = new UploadTokenService(_db);
    }

    public void Dispose()
    {
        _db.Database.CloseConnection();
        _db.Dispose();
    }

    // ── CreateAsync ─────────────────────────────────────────────────────

    [Fact]
    public async Task CreateAsync_AdminAuth_ReturnsTokenResponse()
    {
        await SeedBucketAsync("bucket0001", "admin");
        var auth = AuthContext.Admin();
        var request = new CreateUploadTokenRequest();

        var result = await _sut.CreateAsync("bucket0001", request, auth);

        result.Should().NotBeNull();
        result.Token.Should().StartWith("cfu_");
        result.BucketId.Should().Be("bucket0001");
        result.UploadsUsed.Should().Be(0);
        // Default expiry should be ~1 day from now
        result.ExpiresAt.Should().BeCloseTo(DateTime.UtcNow.AddDays(1), TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task CreateAsync_WithMaxUploads_SetsMaxUploads()
    {
        await SeedBucketAsync("bucket0002", "admin");
        var auth = AuthContext.Admin();
        var request = new CreateUploadTokenRequest { MaxUploads = 5 };

        var result = await _sut.CreateAsync("bucket0002", request, auth);

        result.Should().NotBeNull();
        result.MaxUploads.Should().Be(5);
    }

    [Fact]
    public async Task CreateAsync_WithCustomExpiry_ParsesExpiry()
    {
        await SeedBucketAsync("bucket0003", "admin");
        var auth = AuthContext.Admin();
        var request = new CreateUploadTokenRequest { ExpiresIn = "1h" };

        var result = await _sut.CreateAsync("bucket0003", request, auth);

        result.Should().NotBeNull();
        result.ExpiresAt.Should().BeCloseTo(DateTime.UtcNow.AddHours(1), TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task CreateAsync_NonExistentBucket_ReturnsNull()
    {
        var auth = AuthContext.Admin();
        var request = new CreateUploadTokenRequest();

        var result = await _sut.CreateAsync("nonexist01", request, auth);

        result.Should().BeNull();
    }

    [Fact]
    public async Task CreateAsync_NonOwner_ReturnsNull()
    {
        await SeedBucketAsync("bucket0004", "alice");
        var auth = AuthContext.Owner("bob", "cf4_bob12345");
        var request = new CreateUploadTokenRequest();

        var result = await _sut.CreateAsync("bucket0004", request, auth);

        result.Should().BeNull();
    }

    [Fact]
    public async Task CreateAsync_OwnerCanCreate()
    {
        await SeedBucketAsync("bucket0005", "alice");
        var auth = AuthContext.Owner("alice", "cf4_alice123");
        var request = new CreateUploadTokenRequest();

        var result = await _sut.CreateAsync("bucket0005", request, auth);

        result.Should().NotBeNull();
        result.Token.Should().StartWith("cfu_");
    }

    [Fact]
    public async Task CreateAsync_PublicAuth_ReturnsNull()
    {
        await SeedBucketAsync("bucket0006", "admin");
        var auth = AuthContext.Public();
        var request = new CreateUploadTokenRequest();

        var result = await _sut.CreateAsync("bucket0006", request, auth);

        result.Should().BeNull();
    }

    [Fact]
    public async Task CreateAsync_StoresEntityInDatabase()
    {
        await SeedBucketAsync("bucket0007", "admin");
        var auth = AuthContext.Admin();
        var request = new CreateUploadTokenRequest { MaxUploads = 10 };

        var result = await _sut.CreateAsync("bucket0007", request, auth);

        var entity = await _db.UploadTokens.FirstOrDefaultAsync(t => t.Token == result.Token);
        entity.Should().NotBeNull();
        entity!.BucketId.Should().Be("bucket0007");
        entity.MaxUploads.Should().Be(10);
        entity.UploadsUsed.Should().Be(0);
        entity.CreatedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task CreateAsync_InvalidExpiry_ThrowsArgumentException()
    {
        await SeedBucketAsync("bucket0008", "admin");
        var auth = AuthContext.Admin();
        var request = new CreateUploadTokenRequest { ExpiresIn = "invalid" };

        await Assert.ThrowsAsync<ArgumentException>(() => _sut.CreateAsync("bucket0008", request, auth));
    }

    // ── ValidateAsync ───────────────────────────────────────────────────

    [Fact]
    public async Task ValidateAsync_ValidToken_ReturnsTrueWithBucketId()
    {
        await SeedBucketAsync("bucket0010", "admin");
        _db.UploadTokens.Add(new UploadTokenEntity
        {
            Token = "cfu_validtoken00000000000000000000000000000000000000",
            BucketId = "bucket0010",
            ExpiresAt = DateTime.UtcNow.AddDays(1),
            CreatedAt = DateTime.UtcNow
        });
        await _db.SaveChangesAsync();

        var (bucketId, isValid) = await _sut.ValidateAsync("cfu_validtoken00000000000000000000000000000000000000");

        bucketId.Should().Be("bucket0010");
        isValid.Should().BeTrue();
    }

    [Fact]
    public async Task ValidateAsync_ExpiredToken_ReturnsFalse()
    {
        await SeedBucketAsync("bucket0011", "admin");
        _db.UploadTokens.Add(new UploadTokenEntity
        {
            Token = "cfu_expiredtoken000000000000000000000000000000000000",
            BucketId = "bucket0011",
            ExpiresAt = DateTime.UtcNow.AddDays(-1), // expired
            CreatedAt = DateTime.UtcNow.AddDays(-2)
        });
        await _db.SaveChangesAsync();

        var (bucketId, isValid) = await _sut.ValidateAsync("cfu_expiredtoken000000000000000000000000000000000000");

        isValid.Should().BeFalse();
    }

    [Fact]
    public async Task ValidateAsync_NonExistentToken_ReturnsFalse()
    {
        var (bucketId, isValid) = await _sut.ValidateAsync("cfu_nonexistent0000000000000000000000000000000000000");

        bucketId.Should().BeEmpty();
        isValid.Should().BeFalse();
    }

    [Fact]
    public async Task ValidateAsync_MaxUploadsReached_ReturnsFalse()
    {
        await SeedBucketAsync("bucket0012", "admin");
        _db.UploadTokens.Add(new UploadTokenEntity
        {
            Token = "cfu_maxedtoken000000000000000000000000000000000000000",
            BucketId = "bucket0012",
            ExpiresAt = DateTime.UtcNow.AddDays(1),
            MaxUploads = 3,
            UploadsUsed = 3, // reached max
            CreatedAt = DateTime.UtcNow
        });
        await _db.SaveChangesAsync();

        var (_, isValid) = await _sut.ValidateAsync("cfu_maxedtoken000000000000000000000000000000000000000");

        isValid.Should().BeFalse();
    }

    [Fact]
    public async Task ValidateAsync_MaxUploadsNotReached_ReturnsTrue()
    {
        await SeedBucketAsync("bucket0013", "admin");
        _db.UploadTokens.Add(new UploadTokenEntity
        {
            Token = "cfu_partialtoken00000000000000000000000000000000000",
            BucketId = "bucket0013",
            ExpiresAt = DateTime.UtcNow.AddDays(1),
            MaxUploads = 5,
            UploadsUsed = 2,
            CreatedAt = DateTime.UtcNow
        });
        await _db.SaveChangesAsync();

        var (bucketId, isValid) = await _sut.ValidateAsync("cfu_partialtoken00000000000000000000000000000000000");

        bucketId.Should().Be("bucket0013");
        isValid.Should().BeTrue();
    }

    [Fact]
    public async Task ValidateAsync_NoMaxUploads_ReturnsTrue()
    {
        await SeedBucketAsync("bucket0014", "admin");
        _db.UploadTokens.Add(new UploadTokenEntity
        {
            Token = "cfu_nolimittoken0000000000000000000000000000000000",
            BucketId = "bucket0014",
            ExpiresAt = DateTime.UtcNow.AddDays(1),
            MaxUploads = null,
            UploadsUsed = 100, // lots of uploads, but no limit
            CreatedAt = DateTime.UtcNow
        });
        await _db.SaveChangesAsync();

        var (_, isValid) = await _sut.ValidateAsync("cfu_nolimittoken0000000000000000000000000000000000");

        isValid.Should().BeTrue();
    }

    // ── IncrementUsageAsync ────────────────────────────────────────────

    [Fact]
    public async Task IncrementUsageAsync_IncrementsUploadsUsed()
    {
        await SeedBucketAsync("bucket0020", "admin");
        _db.UploadTokens.Add(new UploadTokenEntity
        {
            Token = "cfu_inctoken0000000000000000000000000000000000000000",
            BucketId = "bucket0020",
            ExpiresAt = DateTime.UtcNow.AddDays(1),
            UploadsUsed = 0,
            CreatedAt = DateTime.UtcNow
        });
        await _db.SaveChangesAsync();

        await _sut.IncrementUsageAsync("cfu_inctoken0000000000000000000000000000000000000000", 3);

        // Need to reload to see the change from ExecuteUpdate
        var entity = await _db.UploadTokens
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Token == "cfu_inctoken0000000000000000000000000000000000000000");
        entity!.UploadsUsed.Should().Be(3);
    }

    [Fact]
    public async Task IncrementUsageAsync_AccumulatesCorrectly()
    {
        await SeedBucketAsync("bucket0021", "admin");
        _db.UploadTokens.Add(new UploadTokenEntity
        {
            Token = "cfu_accumtoken000000000000000000000000000000000000000",
            BucketId = "bucket0021",
            ExpiresAt = DateTime.UtcNow.AddDays(1),
            UploadsUsed = 5,
            CreatedAt = DateTime.UtcNow
        });
        await _db.SaveChangesAsync();

        await _sut.IncrementUsageAsync("cfu_accumtoken000000000000000000000000000000000000000", 2);

        var entity = await _db.UploadTokens
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Token == "cfu_accumtoken000000000000000000000000000000000000000");
        entity!.UploadsUsed.Should().Be(7);
    }

    // ── Helpers ──────────────────────────────────────────────────────────

    private async Task SeedBucketAsync(string bucketId, string owner = "admin")
    {
        _db.Buckets.Add(new BucketEntity
        {
            Id = bucketId,
            Name = $"bucket-{bucketId}",
            Owner = owner,
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddDays(7)
        });
        await _db.SaveChangesAsync();
    }
}
