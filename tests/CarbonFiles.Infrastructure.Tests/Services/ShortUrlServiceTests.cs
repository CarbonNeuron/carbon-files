using System.Data;
using CarbonFiles.Core.Interfaces;
using CarbonFiles.Core.Models;
using CarbonFiles.Infrastructure.Data;
using CarbonFiles.Infrastructure.Data.Entities;
using CarbonFiles.Infrastructure.Services;
using Dapper;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace CarbonFiles.Infrastructure.Tests.Services;

public class ShortUrlServiceTests : IDisposable
{
    private readonly SqliteConnection _db;
    private readonly ShortUrlService _sut;

    public ShortUrlServiceTests()
    {
        _db = new SqliteConnection("Data Source=:memory:");
        _db.Open();
        _db.Execute(DatabaseInitializer.Schema);

        _sut = new ShortUrlService(_db, new NullCacheService(), NullLogger<ShortUrlService>.Instance);
    }

    public void Dispose()
    {
        _db.Close();
        _db.Dispose();
    }

    // ── CreateAsync ─────────────────────────────────────────────────────

    [Fact]
    public async Task CreateAsync_Generates6CharCodeAndStoresIt()
    {
        await SeedBucketAsync("bucket0001");

        var code = await _sut.CreateAsync("bucket0001", "test.txt");

        code.Should().HaveLength(6);

        var entity = await _db.QueryFirstOrDefaultAsync<ShortUrlEntity>(
            "SELECT Code, BucketId, FilePath, CreatedAt FROM ShortUrls WHERE Code = @code", new { code });
        entity.Should().NotBeNull();
        entity!.BucketId.Should().Be("bucket0001");
        entity.FilePath.Should().Be("test.txt");
        entity.CreatedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
    }

    // ── ResolveAsync ────────────────────────────────────────────────────

    [Fact]
    public async Task ResolveAsync_ReturnsCorrectUrl()
    {
        await SeedBucketAsync("bucket0002");
        await _db.ExecuteAsync(
            "INSERT INTO ShortUrls (Code, BucketId, FilePath, CreatedAt) VALUES (@Code, @BucketId, @FilePath, @CreatedAt)",
            new { Code = "abc123", BucketId = "bucket0002", FilePath = "hello.txt", CreatedAt = DateTime.UtcNow });

        var url = await _sut.ResolveAsync("abc123");

        url.Should().Be("/api/buckets/bucket0002/files/hello.txt/content");
    }

    [Fact]
    public async Task ResolveAsync_ExpiredBucket_ReturnsNull()
    {
        await _db.ExecuteAsync(
            "INSERT INTO Buckets (Id, Name, Owner, CreatedAt, ExpiresAt) VALUES (@Id, @Name, @Owner, @CreatedAt, @ExpiresAt)",
            new { Id = "expired001", Name = "expired", Owner = "admin", CreatedAt = DateTime.UtcNow.AddDays(-10), ExpiresAt = DateTime.UtcNow.AddDays(-1) });
        await _db.ExecuteAsync(
            "INSERT INTO ShortUrls (Code, BucketId, FilePath, CreatedAt) VALUES (@Code, @BucketId, @FilePath, @CreatedAt)",
            new { Code = "exp123", BucketId = "expired001", FilePath = "file.txt", CreatedAt = DateTime.UtcNow });

        var url = await _sut.ResolveAsync("exp123");

        url.Should().BeNull();
    }

    [Fact]
    public async Task ResolveAsync_NonexistentCode_ReturnsNull()
    {
        var url = await _sut.ResolveAsync("zzzzzz");

        url.Should().BeNull();
    }

    [Fact]
    public async Task ResolveAsync_BucketDeleted_ReturnsNull()
    {
        // Short URL exists but bucket has been removed
        await _db.ExecuteAsync(
            "INSERT INTO ShortUrls (Code, BucketId, FilePath, CreatedAt) VALUES (@Code, @BucketId, @FilePath, @CreatedAt)",
            new { Code = "orphan1", BucketId = "deleted001", FilePath = "orphan.txt", CreatedAt = DateTime.UtcNow });

        var url = await _sut.ResolveAsync("orphan1");

        url.Should().BeNull();
    }

    // ── DeleteAsync ─────────────────────────────────────────────────────

    [Fact]
    public async Task DeleteAsync_RemovesShortUrl()
    {
        await SeedBucketAsync("bucket0003", "admin");
        await _db.ExecuteAsync(
            "INSERT INTO ShortUrls (Code, BucketId, FilePath, CreatedAt) VALUES (@Code, @BucketId, @FilePath, @CreatedAt)",
            new { Code = "del123", BucketId = "bucket0003", FilePath = "file.txt", CreatedAt = DateTime.UtcNow });

        var auth = AuthContext.Admin();
        var result = await _sut.DeleteAsync("del123", auth);

        result.Should().BeTrue();
        var remaining = await _db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM ShortUrls WHERE Code = 'del123'");
        remaining.Should().Be(0);
    }

    [Fact]
    public async Task DeleteAsync_NonOwner_ReturnsFalse()
    {
        await _db.ExecuteAsync(
            "INSERT INTO Buckets (Id, Name, Owner, CreatedAt, ExpiresAt) VALUES (@Id, @Name, @Owner, @CreatedAt, @ExpiresAt)",
            new { Id = "bucket0004", Name = "alice-bucket", Owner = "alice", CreatedAt = DateTime.UtcNow, ExpiresAt = DateTime.UtcNow.AddDays(7) });
        await _db.ExecuteAsync(
            "INSERT INTO ShortUrls (Code, BucketId, FilePath, CreatedAt) VALUES (@Code, @BucketId, @FilePath, @CreatedAt)",
            new { Code = "own123", BucketId = "bucket0004", FilePath = "file.txt", CreatedAt = DateTime.UtcNow });

        var auth = AuthContext.Owner("bob", "cf4_bob12345");
        var result = await _sut.DeleteAsync("own123", auth);

        result.Should().BeFalse();
        // Short URL should still exist
        var remaining = await _db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM ShortUrls WHERE Code = 'own123'");
        remaining.Should().Be(1);
    }

    [Fact]
    public async Task DeleteAsync_OwnerCanDelete()
    {
        await _db.ExecuteAsync(
            "INSERT INTO Buckets (Id, Name, Owner, CreatedAt, ExpiresAt) VALUES (@Id, @Name, @Owner, @CreatedAt, @ExpiresAt)",
            new { Id = "bucket0005", Name = "alice-bucket", Owner = "alice", CreatedAt = DateTime.UtcNow, ExpiresAt = DateTime.UtcNow.AddDays(7) });
        await _db.ExecuteAsync(
            "INSERT INTO ShortUrls (Code, BucketId, FilePath, CreatedAt) VALUES (@Code, @BucketId, @FilePath, @CreatedAt)",
            new { Code = "own456", BucketId = "bucket0005", FilePath = "file.txt", CreatedAt = DateTime.UtcNow });

        var auth = AuthContext.Owner("alice", "cf4_alice123");
        var result = await _sut.DeleteAsync("own456", auth);

        result.Should().BeTrue();
        var remaining = await _db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM ShortUrls WHERE Code = 'own456'");
        remaining.Should().Be(0);
    }

    [Fact]
    public async Task DeleteAsync_NonexistentCode_ReturnsFalse()
    {
        var auth = AuthContext.Admin();
        var result = await _sut.DeleteAsync("nocode", auth);

        result.Should().BeFalse();
    }

    [Fact]
    public async Task DeleteAsync_BucketDeleted_ReturnsFalse()
    {
        // Short URL exists but bucket is gone
        await _db.ExecuteAsync(
            "INSERT INTO ShortUrls (Code, BucketId, FilePath, CreatedAt) VALUES (@Code, @BucketId, @FilePath, @CreatedAt)",
            new { Code = "orphn2", BucketId = "deleted002", FilePath = "orphan.txt", CreatedAt = DateTime.UtcNow });

        var auth = AuthContext.Admin();
        var result = await _sut.DeleteAsync("orphn2", auth);

        result.Should().BeFalse();
    }

    // ── Helpers ──────────────────────────────────────────────────────────

    private async Task SeedBucketAsync(string bucketId, string owner = "admin")
    {
        await _db.ExecuteAsync(
            "INSERT INTO Buckets (Id, Name, Owner, CreatedAt, ExpiresAt) VALUES (@Id, @Name, @Owner, @CreatedAt, @ExpiresAt)",
            new { Id = bucketId, Name = $"bucket-{bucketId}", Owner = owner, CreatedAt = DateTime.UtcNow, ExpiresAt = DateTime.UtcNow.AddDays(7) });
    }
}
