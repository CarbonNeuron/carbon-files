using CarbonFiles.Core.Configuration;
using CarbonFiles.Core.Interfaces;
using CarbonFiles.Core.Models;
using CarbonFiles.Core.Models.Requests;
using CarbonFiles.Core.Models.Responses;
using CarbonFiles.Infrastructure.Data;
using CarbonFiles.Infrastructure.Data.Entities;
using CarbonFiles.Infrastructure.Services;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace CarbonFiles.Infrastructure.Tests.Services;

internal sealed class NullNotificationService : INotificationService
{
    public Task NotifyFileCreated(string bucketId, BucketFile file) => Task.CompletedTask;
    public Task NotifyFileUpdated(string bucketId, BucketFile file) => Task.CompletedTask;
    public Task NotifyFileDeleted(string bucketId, string path) => Task.CompletedTask;
    public Task NotifyBucketCreated(Bucket bucket) => Task.CompletedTask;
    public Task NotifyBucketUpdated(string bucketId, BucketChanges changes) => Task.CompletedTask;
    public Task NotifyBucketDeleted(string bucketId) => Task.CompletedTask;
}

public class BucketServiceTests : IDisposable
{
    private readonly CarbonFilesDbContext _db;
    private readonly BucketService _sut;
    private readonly string _tempDir;

    public BucketServiceTests()
    {
        var dbOptions = new DbContextOptionsBuilder<CarbonFilesDbContext>()
            .UseSqlite("Data Source=:memory:")
            .Options;

        _db = new CarbonFilesDbContext(dbOptions);
        _db.Database.OpenConnection();
        _db.Database.EnsureCreated();

        _tempDir = Path.Combine(Path.GetTempPath(), $"cf_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);

        var options = Options.Create(new CarbonFilesOptions { DataDir = _tempDir });
        _sut = new BucketService(_db, options, new NullNotificationService(), NullLogger<BucketService>.Instance);
    }

    public void Dispose()
    {
        _db.Database.CloseConnection();
        _db.Dispose();
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    // ── CreateAsync ─────────────────────────────────────────────────────

    [Fact]
    public async Task CreateAsync_AdminAuth_SetsOwnerToAdmin()
    {
        var auth = AuthContext.Admin();
        var request = new CreateBucketRequest { Name = "admin-bucket" };

        var result = await _sut.CreateAsync(request, auth);

        result.Owner.Should().Be("admin");
        result.Name.Should().Be("admin-bucket");
        result.Id.Should().HaveLength(10);
        result.CreatedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task CreateAsync_OwnerAuth_SetsOwnerName()
    {
        var auth = AuthContext.Owner("my-agent", "cf4_12345678");
        var request = new CreateBucketRequest { Name = "agent-bucket" };

        var result = await _sut.CreateAsync(request, auth);

        result.Owner.Should().Be("my-agent");
    }

    [Fact]
    public async Task CreateAsync_CreatesDirectoryOnDisk()
    {
        var auth = AuthContext.Admin();
        var request = new CreateBucketRequest { Name = "dir-test" };

        var result = await _sut.CreateAsync(request, auth);

        var bucketDir = Path.Combine(_tempDir, result.Id);
        Directory.Exists(bucketDir).Should().BeTrue();
    }

    [Fact]
    public async Task CreateAsync_ParsesExpiresIn()
    {
        var auth = AuthContext.Admin();
        var request = new CreateBucketRequest { Name = "expiry-test", ExpiresIn = "1d" };

        var result = await _sut.CreateAsync(request, auth);

        result.ExpiresAt.Should().BeCloseTo(DateTime.UtcNow.AddDays(1), TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task CreateAsync_NeverExpiry_SetsNull()
    {
        var auth = AuthContext.Admin();
        var request = new CreateBucketRequest { Name = "never-expire", ExpiresIn = "never" };

        var result = await _sut.CreateAsync(request, auth);

        result.ExpiresAt.Should().BeNull();
    }

    [Fact]
    public async Task CreateAsync_DefaultExpiry_Is1Week()
    {
        var auth = AuthContext.Admin();
        var request = new CreateBucketRequest { Name = "default-expire" };

        var result = await _sut.CreateAsync(request, auth);

        result.ExpiresAt.Should().BeCloseTo(DateTime.UtcNow.AddDays(7), TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task CreateAsync_InvalidExpiry_ThrowsArgumentException()
    {
        var auth = AuthContext.Admin();
        var request = new CreateBucketRequest { Name = "bad-expiry", ExpiresIn = "xyz" };

        await Assert.ThrowsAsync<ArgumentException>(() => _sut.CreateAsync(request, auth));
    }

    [Fact]
    public async Task CreateAsync_StoresEntityInDatabase()
    {
        var auth = AuthContext.Admin();
        var request = new CreateBucketRequest { Name = "stored", Description = "desc" };

        var result = await _sut.CreateAsync(request, auth);

        var entity = await _db.Buckets.FindAsync(new object[] { result.Id }, TestContext.Current.CancellationToken);
        entity.Should().NotBeNull();
        entity!.Name.Should().Be("stored");
        entity.Description.Should().Be("desc");
        entity.Owner.Should().Be("admin");
    }

    [Fact]
    public async Task CreateAsync_OwnerAuth_SetsKeyPrefix()
    {
        var auth = AuthContext.Owner("agent", "cf4_aabbccdd");
        var request = new CreateBucketRequest { Name = "keyed" };

        var result = await _sut.CreateAsync(request, auth);

        var entity = await _db.Buckets.FindAsync(new object[] { result.Id }, TestContext.Current.CancellationToken);
        entity!.OwnerKeyPrefix.Should().Be("cf4_aabbccdd");
    }

    // ── ListAsync ───────────────────────────────────────────────────────

    [Fact]
    public async Task ListAsync_AdminSeesAll()
    {
        await SeedBucketsAsync();
        var auth = AuthContext.Admin();

        var result = await _sut.ListAsync(new PaginationParams(), auth);

        result.Total.Should().Be(3);
        result.Items.Should().HaveCount(3);
    }

    [Fact]
    public async Task ListAsync_OwnerSeesOnlyOwn()
    {
        await SeedBucketsAsync();
        var auth = AuthContext.Owner("alice", "cf4_alice123");

        var result = await _sut.ListAsync(new PaginationParams(), auth);

        result.Total.Should().Be(2);
        result.Items.Should().AllSatisfy(b => b.Owner.Should().Be("alice"));
    }

    [Fact]
    public async Task ListAsync_ExcludesExpiredByDefault()
    {
        _db.Buckets.Add(new BucketEntity
        {
            Id = "expired001",
            Name = "expired",
            Owner = "admin",
            CreatedAt = DateTime.UtcNow.AddDays(-10),
            ExpiresAt = DateTime.UtcNow.AddDays(-1) // expired yesterday
        });
        _db.Buckets.Add(new BucketEntity
        {
            Id = "valid00001",
            Name = "valid",
            Owner = "admin",
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddDays(7)
        });
        await _db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var auth = AuthContext.Admin();
        var result = await _sut.ListAsync(new PaginationParams(), auth, includeExpired: false);

        result.Items.Should().NotContain(b => b.Id == "expired001");
        result.Items.Should().Contain(b => b.Id == "valid00001");
    }

    [Fact]
    public async Task ListAsync_IncludeExpiredShowsAll()
    {
        _db.Buckets.Add(new BucketEntity
        {
            Id = "expired002",
            Name = "expired-inc",
            Owner = "admin",
            CreatedAt = DateTime.UtcNow.AddDays(-10),
            ExpiresAt = DateTime.UtcNow.AddDays(-1)
        });
        _db.Buckets.Add(new BucketEntity
        {
            Id = "valid00002",
            Name = "valid-inc",
            Owner = "admin",
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddDays(7)
        });
        await _db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var auth = AuthContext.Admin();
        var result = await _sut.ListAsync(new PaginationParams(), auth, includeExpired: true);

        result.Items.Should().Contain(b => b.Id == "expired002");
        result.Items.Should().Contain(b => b.Id == "valid00002");
    }

    [Fact]
    public async Task ListAsync_RespectsLimitAndOffset()
    {
        await SeedBucketsAsync();
        var auth = AuthContext.Admin();

        var result = await _sut.ListAsync(
            new PaginationParams { Limit = 1, Offset = 1, Sort = "name", Order = "asc" },
            auth);

        result.Total.Should().Be(3);
        result.Items.Should().HaveCount(1);
        result.Limit.Should().Be(1);
        result.Offset.Should().Be(1);
    }

    [Fact]
    public async Task ListAsync_SortByName()
    {
        await SeedBucketsAsync();
        var auth = AuthContext.Admin();

        var result = await _sut.ListAsync(
            new PaginationParams { Sort = "name", Order = "asc" },
            auth);

        result.Items.Select(b => b.Name).Should().BeInAscendingOrder();
    }

    [Fact]
    public async Task ListAsync_SortByTotalSize()
    {
        _db.Buckets.Add(new BucketEntity { Id = "size00001", Name = "small", Owner = "admin", CreatedAt = DateTime.UtcNow, TotalSize = 100 });
        _db.Buckets.Add(new BucketEntity { Id = "size00002", Name = "big", Owner = "admin", CreatedAt = DateTime.UtcNow, TotalSize = 10000 });
        _db.Buckets.Add(new BucketEntity { Id = "size00003", Name = "medium", Owner = "admin", CreatedAt = DateTime.UtcNow, TotalSize = 1000 });
        await _db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var auth = AuthContext.Admin();
        var result = await _sut.ListAsync(
            new PaginationParams { Sort = "total_size", Order = "asc" },
            auth);

        result.Items.Where(b => b.Id.StartsWith("size"))
              .Select(b => b.TotalSize)
              .Should().BeInAscendingOrder();
    }

    // ── GetByIdAsync ────────────────────────────────────────────────────

    [Fact]
    public async Task GetByIdAsync_ExistingBucket_ReturnsBucketWithFiles()
    {
        _db.Buckets.Add(new BucketEntity
        {
            Id = "get0000001",
            Name = "get-test",
            Owner = "admin",
            CreatedAt = DateTime.UtcNow,
            FileCount = 1,
            TotalSize = 512
        });
        _db.Files.Add(new FileEntity
        {
            BucketId = "get0000001",
            Path = "hello.txt",
            Name = "hello.txt",
            Size = 512,
            MimeType = "text/plain",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });
        await _db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var result = await _sut.GetByIdAsync("get0000001");

        result.Should().NotBeNull();
        result!.Id.Should().Be("get0000001");
        result.Name.Should().Be("get-test");
        result.Files.Should().HaveCount(1);
        result.Files[0].Path.Should().Be("hello.txt");
        result.HasMoreFiles.Should().BeFalse();
    }

    [Fact]
    public async Task GetByIdAsync_NonExistent_ReturnsNull()
    {
        var result = await _sut.GetByIdAsync("nonexistent");
        result.Should().BeNull();
    }

    [Fact]
    public async Task GetByIdAsync_ExpiredBucket_ReturnsNull()
    {
        _db.Buckets.Add(new BucketEntity
        {
            Id = "expired010",
            Name = "expired-get",
            Owner = "admin",
            CreatedAt = DateTime.UtcNow.AddDays(-10),
            ExpiresAt = DateTime.UtcNow.AddDays(-1)
        });
        await _db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var result = await _sut.GetByIdAsync("expired010");
        result.Should().BeNull();
    }

    [Fact]
    public async Task GetByIdAsync_LimitsTo100Files()
    {
        _db.Buckets.Add(new BucketEntity
        {
            Id = "many000001",
            Name = "many-files",
            Owner = "admin",
            CreatedAt = DateTime.UtcNow,
            FileCount = 105
        });
        for (int i = 0; i < 105; i++)
        {
            _db.Files.Add(new FileEntity
            {
                BucketId = "many000001",
                Path = $"file{i:D4}.txt",
                Name = $"file{i:D4}.txt",
                Size = 100,
                MimeType = "text/plain",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
        }
        await _db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var result = await _sut.GetByIdAsync("many000001");

        result.Should().NotBeNull();
        result!.Files.Should().HaveCount(100);
        result.HasMoreFiles.Should().BeTrue();
    }

    // ── UpdateAsync ─────────────────────────────────────────────────────

    [Fact]
    public async Task UpdateAsync_UpdatesName()
    {
        _db.Buckets.Add(new BucketEntity
        {
            Id = "upd0000001",
            Name = "original",
            Owner = "admin",
            CreatedAt = DateTime.UtcNow
        });
        await _db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var auth = AuthContext.Admin();
        var result = await _sut.UpdateAsync("upd0000001",
            new UpdateBucketRequest { Name = "renamed" }, auth);

        result.Should().NotBeNull();
        result!.Name.Should().Be("renamed");
    }

    [Fact]
    public async Task UpdateAsync_UpdatesDescription()
    {
        _db.Buckets.Add(new BucketEntity
        {
            Id = "upd0000002",
            Name = "desc-test",
            Owner = "admin",
            CreatedAt = DateTime.UtcNow
        });
        await _db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var auth = AuthContext.Admin();
        var result = await _sut.UpdateAsync("upd0000002",
            new UpdateBucketRequest { Description = "new desc" }, auth);

        result.Should().NotBeNull();
        result!.Description.Should().Be("new desc");
    }

    [Fact]
    public async Task UpdateAsync_UpdatesExpiry()
    {
        _db.Buckets.Add(new BucketEntity
        {
            Id = "upd0000003",
            Name = "exp-test",
            Owner = "admin",
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddDays(1)
        });
        await _db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var auth = AuthContext.Admin();
        var result = await _sut.UpdateAsync("upd0000003",
            new UpdateBucketRequest { ExpiresIn = "never" }, auth);

        result.Should().NotBeNull();
        result!.ExpiresAt.Should().BeNull();
    }

    [Fact]
    public async Task UpdateAsync_NonExistent_ReturnsNull()
    {
        var auth = AuthContext.Admin();
        var result = await _sut.UpdateAsync("nonexist01",
            new UpdateBucketRequest { Name = "nope" }, auth);

        result.Should().BeNull();
    }

    [Fact]
    public async Task UpdateAsync_NonOwner_ReturnsNull()
    {
        _db.Buckets.Add(new BucketEntity
        {
            Id = "upd0000004",
            Name = "not-yours",
            Owner = "alice",
            CreatedAt = DateTime.UtcNow
        });
        await _db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var auth = AuthContext.Owner("bob", "cf4_bob12345");
        var result = await _sut.UpdateAsync("upd0000004",
            new UpdateBucketRequest { Name = "stolen" }, auth);

        result.Should().BeNull();
    }

    [Fact]
    public async Task UpdateAsync_OwnerCanUpdate()
    {
        _db.Buckets.Add(new BucketEntity
        {
            Id = "upd0000005",
            Name = "mine",
            Owner = "alice",
            CreatedAt = DateTime.UtcNow
        });
        await _db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var auth = AuthContext.Owner("alice", "cf4_alice123");
        var result = await _sut.UpdateAsync("upd0000005",
            new UpdateBucketRequest { Name = "still-mine" }, auth);

        result.Should().NotBeNull();
        result!.Name.Should().Be("still-mine");
    }

    // ── DeleteAsync ─────────────────────────────────────────────────────

    [Fact]
    public async Task DeleteAsync_ExistingBucket_DeletesAllRelated()
    {
        _db.Buckets.Add(new BucketEntity
        {
            Id = "del0000001",
            Name = "to-delete",
            Owner = "admin",
            CreatedAt = DateTime.UtcNow
        });
        _db.Files.Add(new FileEntity
        {
            BucketId = "del0000001",
            Path = "file.txt",
            Name = "file.txt",
            Size = 100,
            MimeType = "text/plain",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });
        _db.ShortUrls.Add(new ShortUrlEntity
        {
            Code = "abc123",
            BucketId = "del0000001",
            FilePath = "file.txt",
            CreatedAt = DateTime.UtcNow
        });
        _db.UploadTokens.Add(new UploadTokenEntity
        {
            Token = "cfu_testtoken1234567890123456789012345678901234",
            BucketId = "del0000001",
            ExpiresAt = DateTime.UtcNow.AddDays(1),
            CreatedAt = DateTime.UtcNow
        });
        await _db.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Create the bucket directory
        Directory.CreateDirectory(Path.Combine(_tempDir, "del0000001"));

        var auth = AuthContext.Admin();
        var result = await _sut.DeleteAsync("del0000001", auth);

        result.Should().BeTrue();

        (await _db.Buckets.FindAsync(new object[] { "del0000001" }, TestContext.Current.CancellationToken)).Should().BeNull();
        (await _db.Files.AnyAsync(f => f.BucketId == "del0000001", TestContext.Current.CancellationToken)).Should().BeFalse();
        (await _db.ShortUrls.AnyAsync(s => s.BucketId == "del0000001", TestContext.Current.CancellationToken)).Should().BeFalse();
        (await _db.UploadTokens.AnyAsync(t => t.BucketId == "del0000001", TestContext.Current.CancellationToken)).Should().BeFalse();
        Directory.Exists(Path.Combine(_tempDir, "del0000001")).Should().BeFalse();
    }

    [Fact]
    public async Task DeleteAsync_NonExistent_ReturnsFalse()
    {
        var auth = AuthContext.Admin();
        var result = await _sut.DeleteAsync("nonexist02", auth);

        result.Should().BeFalse();
    }

    [Fact]
    public async Task DeleteAsync_NonOwner_ReturnsFalse()
    {
        _db.Buckets.Add(new BucketEntity
        {
            Id = "del0000002",
            Name = "not-yours",
            Owner = "alice",
            CreatedAt = DateTime.UtcNow
        });
        await _db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var auth = AuthContext.Owner("bob", "cf4_bob12345");
        var result = await _sut.DeleteAsync("del0000002", auth);

        result.Should().BeFalse();
        // Bucket should still exist
        (await _db.Buckets.FindAsync(new object[] { "del0000002" }, TestContext.Current.CancellationToken)).Should().NotBeNull();
    }

    [Fact]
    public async Task DeleteAsync_OwnerCanDelete()
    {
        _db.Buckets.Add(new BucketEntity
        {
            Id = "del0000003",
            Name = "mine-delete",
            Owner = "alice",
            CreatedAt = DateTime.UtcNow
        });
        await _db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var auth = AuthContext.Owner("alice", "cf4_alice123");
        var result = await _sut.DeleteAsync("del0000003", auth);

        result.Should().BeTrue();
        (await _db.Buckets.FindAsync(new object[] { "del0000003" }, TestContext.Current.CancellationToken)).Should().BeNull();
    }

    // ── GetSummaryAsync ─────────────────────────────────────────────────

    [Fact]
    public async Task GetSummaryAsync_ReturnsSummaryText()
    {
        _db.Buckets.Add(new BucketEntity
        {
            Id = "sum0000001",
            Name = "summary-test",
            Owner = "admin",
            CreatedAt = new DateTime(2025, 1, 15, 0, 0, 0, DateTimeKind.Utc),
            ExpiresAt = DateTime.UtcNow.AddDays(30),
            FileCount = 2,
            TotalSize = 1536
        });
        _db.Files.Add(new FileEntity
        {
            BucketId = "sum0000001",
            Path = "doc.pdf",
            Name = "doc.pdf",
            Size = 1024,
            MimeType = "application/pdf",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });
        _db.Files.Add(new FileEntity
        {
            BucketId = "sum0000001",
            Path = "img.png",
            Name = "img.png",
            Size = 512,
            MimeType = "image/png",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });
        await _db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var result = await _sut.GetSummaryAsync("sum0000001");

        result.Should().NotBeNull();
        result.Should().Contain("Bucket: summary-test");
        result.Should().Contain("Owner: admin");
        result.Should().Contain("Files: 2 (1.5 KB)");
        result.Should().Contain("Created: 2025-01-15");
        result.Should().Contain("Expires:");
        result.Should().Contain("doc.pdf (1.0 KB)");
        result.Should().Contain("img.png (512 B)");
    }

    [Fact]
    public async Task GetSummaryAsync_NeverExpiry_ShowsNever()
    {
        _db.Buckets.Add(new BucketEntity
        {
            Id = "sum0000002",
            Name = "no-expire-summary",
            Owner = "admin",
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = null,
            FileCount = 0,
            TotalSize = 0
        });
        await _db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var result = await _sut.GetSummaryAsync("sum0000002");

        result.Should().Contain("Expires: never");
    }

    [Fact]
    public async Task GetSummaryAsync_NonExistent_ReturnsNull()
    {
        var result = await _sut.GetSummaryAsync("nonexist03");
        result.Should().BeNull();
    }

    [Fact]
    public async Task GetSummaryAsync_ExpiredBucket_ReturnsNull()
    {
        _db.Buckets.Add(new BucketEntity
        {
            Id = "sum0000003",
            Name = "expired-summary",
            Owner = "admin",
            CreatedAt = DateTime.UtcNow.AddDays(-10),
            ExpiresAt = DateTime.UtcNow.AddDays(-1)
        });
        await _db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var result = await _sut.GetSummaryAsync("sum0000003");
        result.Should().BeNull();
    }

    // ── Helpers ──────────────────────────────────────────────────────────

    private async Task SeedBucketsAsync()
    {
        _db.Buckets.Add(new BucketEntity
        {
            Id = "seed000001",
            Name = "alpha",
            Owner = "alice",
            OwnerKeyPrefix = "cf4_alice123",
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddDays(7),
            TotalSize = 100
        });
        _db.Buckets.Add(new BucketEntity
        {
            Id = "seed000002",
            Name = "bravo",
            Owner = "alice",
            OwnerKeyPrefix = "cf4_alice123",
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddDays(7),
            TotalSize = 200
        });
        _db.Buckets.Add(new BucketEntity
        {
            Id = "seed000003",
            Name = "charlie",
            Owner = "bob",
            OwnerKeyPrefix = "cf4_bob12345",
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddDays(7),
            TotalSize = 300
        });
        await _db.SaveChangesAsync(TestContext.Current.CancellationToken);
    }
}
