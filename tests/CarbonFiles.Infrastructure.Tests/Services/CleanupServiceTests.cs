using System.Data;
using CarbonFiles.Core.Configuration;
using CarbonFiles.Core.Interfaces;
using CarbonFiles.Infrastructure.Data;
using CarbonFiles.Infrastructure.Data.Entities;
using CarbonFiles.Infrastructure.Services;
using Dapper;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace CarbonFiles.Infrastructure.Tests.Services;

public class CleanupServiceTests : IDisposable
{
    private readonly SqliteConnection _keepAliveConnection;
    private readonly string _connectionString;
    private readonly string _tempDir;
    private readonly ServiceProvider _serviceProvider;
    private readonly CleanupService _sut;

    public CleanupServiceTests()
    {
        // Use a shared named in-memory SQLite database so multiple connections
        // can share the same data. The _keepAliveConnection keeps the DB alive.
        var dbName = $"CleanupTest_{Guid.NewGuid():N}";
        _connectionString = $"Data Source={dbName};Mode=Memory;Cache=Shared";

        _keepAliveConnection = new SqliteConnection(_connectionString);
        _keepAliveConnection.Open();

        // Create schema
        _keepAliveConnection.Execute(DatabaseInitializer.Schema);

        _tempDir = Path.Combine(Path.GetTempPath(), $"cf_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);

        var cfOptions = Options.Create(new CarbonFilesOptions
        {
            DataDir = _tempDir,
            CleanupIntervalMinutes = 1
        });

        // Build a service provider that the CleanupService can create scopes from.
        var services = new ServiceCollection();
        services.AddScoped<IDbConnection>(_ =>
        {
            var conn = new SqliteConnection(_connectionString);
            conn.Open();
            return conn;
        });
        services.AddSingleton(new FileStorageService(cfOptions, NullLogger<FileStorageService>.Instance));
        services.AddSingleton<ICacheService>(new NullCacheService());
        services.AddScoped<CleanupRepository>();
        _serviceProvider = services.BuildServiceProvider();

        _sut = new CleanupService(
            _serviceProvider,
            cfOptions,
            NullLogger<CleanupService>.Instance);
    }

    public void Dispose()
    {
        _serviceProvider.Dispose();
        _keepAliveConnection.Close();
        _keepAliveConnection.Dispose();
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    // ── CleanupExpiredBucketsAsync ───────────────────────────────────────

    [Fact]
    public async Task CleanupExpiredBucketsAsync_RemovesExpiredBucket()
    {
        _keepAliveConnection.Execute(
            "INSERT INTO Buckets (Id, Name, Owner, CreatedAt, ExpiresAt) VALUES (@Id, @Name, @Owner, @CreatedAt, @ExpiresAt)",
            new { Id = "exp0000001", Name = "expired-bucket", Owner = "admin", CreatedAt = DateTime.UtcNow.AddDays(-10), ExpiresAt = DateTime.UtcNow.AddDays(-1) });

        await _sut.CleanupExpiredBucketsAsync(TestContext.Current.CancellationToken);

        _keepAliveConnection.ExecuteScalar<int>("SELECT COUNT(*) FROM Buckets WHERE Id = 'exp0000001'").Should().Be(0);
    }

    [Fact]
    public async Task CleanupExpiredBucketsAsync_DoesNotRemoveActiveBucket()
    {
        _keepAliveConnection.Execute(
            "INSERT INTO Buckets (Id, Name, Owner, CreatedAt, ExpiresAt) VALUES (@Id, @Name, @Owner, @CreatedAt, @ExpiresAt)",
            new { Id = "act0000001", Name = "active-bucket", Owner = "admin", CreatedAt = DateTime.UtcNow, ExpiresAt = DateTime.UtcNow.AddDays(7) });

        await _sut.CleanupExpiredBucketsAsync(TestContext.Current.CancellationToken);

        _keepAliveConnection.ExecuteScalar<int>("SELECT COUNT(*) FROM Buckets WHERE Id = 'act0000001'").Should().Be(1);
    }

    [Fact]
    public async Task CleanupExpiredBucketsAsync_DoesNotRemoveNeverExpireBucket()
    {
        _keepAliveConnection.Execute(
            "INSERT INTO Buckets (Id, Name, Owner, CreatedAt) VALUES (@Id, @Name, @Owner, @CreatedAt)",
            new { Id = "nev0000001", Name = "never-expire", Owner = "admin", CreatedAt = DateTime.UtcNow });

        await _sut.CleanupExpiredBucketsAsync(TestContext.Current.CancellationToken);

        _keepAliveConnection.ExecuteScalar<int>("SELECT COUNT(*) FROM Buckets WHERE Id = 'nev0000001'").Should().Be(1);
    }

    [Fact]
    public async Task CleanupExpiredBucketsAsync_RemovesAssociatedFiles()
    {
        _keepAliveConnection.Execute(
            "INSERT INTO Buckets (Id, Name, Owner, CreatedAt, ExpiresAt, FileCount, TotalSize) VALUES (@Id, @Name, @Owner, @CreatedAt, @ExpiresAt, @FileCount, @TotalSize)",
            new { Id = "exp0000002", Name = "expired-with-files", Owner = "admin", CreatedAt = DateTime.UtcNow.AddDays(-10), ExpiresAt = DateTime.UtcNow.AddDays(-1), FileCount = 2, TotalSize = 200L });
        _keepAliveConnection.Execute(
            "INSERT INTO Files (BucketId, Path, Name, Size, MimeType, CreatedAt, UpdatedAt) VALUES (@BucketId, @Path, @Name, @Size, @MimeType, @CreatedAt, @UpdatedAt)",
            new { BucketId = "exp0000002", Path = "file1.txt", Name = "file1.txt", Size = 100L, MimeType = "text/plain", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow });
        _keepAliveConnection.Execute(
            "INSERT INTO Files (BucketId, Path, Name, Size, MimeType, CreatedAt, UpdatedAt) VALUES (@BucketId, @Path, @Name, @Size, @MimeType, @CreatedAt, @UpdatedAt)",
            new { BucketId = "exp0000002", Path = "file2.txt", Name = "file2.txt", Size = 100L, MimeType = "text/plain", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow });

        await _sut.CleanupExpiredBucketsAsync(TestContext.Current.CancellationToken);

        _keepAliveConnection.ExecuteScalar<int>("SELECT COUNT(*) FROM Files WHERE BucketId = 'exp0000002'").Should().Be(0);
    }

    [Fact]
    public async Task CleanupExpiredBucketsAsync_RemovesAssociatedShortUrls()
    {
        _keepAliveConnection.Execute(
            "INSERT INTO Buckets (Id, Name, Owner, CreatedAt, ExpiresAt) VALUES (@Id, @Name, @Owner, @CreatedAt, @ExpiresAt)",
            new { Id = "exp0000003", Name = "expired-with-urls", Owner = "admin", CreatedAt = DateTime.UtcNow.AddDays(-10), ExpiresAt = DateTime.UtcNow.AddDays(-1) });
        _keepAliveConnection.Execute(
            "INSERT INTO ShortUrls (Code, BucketId, FilePath, CreatedAt) VALUES (@Code, @BucketId, @FilePath, @CreatedAt)",
            new { Code = "abc123", BucketId = "exp0000003", FilePath = "file.txt", CreatedAt = DateTime.UtcNow });

        await _sut.CleanupExpiredBucketsAsync(TestContext.Current.CancellationToken);

        _keepAliveConnection.ExecuteScalar<int>("SELECT COUNT(*) FROM ShortUrls WHERE BucketId = 'exp0000003'").Should().Be(0);
    }

    [Fact]
    public async Task CleanupExpiredBucketsAsync_RemovesAssociatedUploadTokens()
    {
        _keepAliveConnection.Execute(
            "INSERT INTO Buckets (Id, Name, Owner, CreatedAt, ExpiresAt) VALUES (@Id, @Name, @Owner, @CreatedAt, @ExpiresAt)",
            new { Id = "exp0000004", Name = "expired-with-tokens", Owner = "admin", CreatedAt = DateTime.UtcNow.AddDays(-10), ExpiresAt = DateTime.UtcNow.AddDays(-1) });
        _keepAliveConnection.Execute(
            "INSERT INTO UploadTokens (Token, BucketId, ExpiresAt, CreatedAt) VALUES (@Token, @BucketId, @ExpiresAt, @CreatedAt)",
            new { Token = "cfu_testtoken1234567890123456789012345678901234", BucketId = "exp0000004", ExpiresAt = DateTime.UtcNow.AddDays(1), CreatedAt = DateTime.UtcNow });

        await _sut.CleanupExpiredBucketsAsync(TestContext.Current.CancellationToken);

        _keepAliveConnection.ExecuteScalar<int>("SELECT COUNT(*) FROM UploadTokens WHERE BucketId = 'exp0000004'").Should().Be(0);
    }

    [Fact]
    public async Task CleanupExpiredBucketsAsync_DeletesBucketDirectory()
    {
        var bucketDir = Path.Combine(_tempDir, "exp0000005");
        Directory.CreateDirectory(bucketDir);
        File.WriteAllText(Path.Combine(bucketDir, "test.txt"), "hello");

        _keepAliveConnection.Execute(
            "INSERT INTO Buckets (Id, Name, Owner, CreatedAt, ExpiresAt) VALUES (@Id, @Name, @Owner, @CreatedAt, @ExpiresAt)",
            new { Id = "exp0000005", Name = "expired-with-dir", Owner = "admin", CreatedAt = DateTime.UtcNow.AddDays(-10), ExpiresAt = DateTime.UtcNow.AddDays(-1) });

        await _sut.CleanupExpiredBucketsAsync(TestContext.Current.CancellationToken);

        Directory.Exists(bucketDir).Should().BeFalse();
    }

    [Fact]
    public async Task CleanupExpiredBucketsAsync_NoExpiredBuckets_DoesNothing()
    {
        _keepAliveConnection.Execute(
            "INSERT INTO Buckets (Id, Name, Owner, CreatedAt, ExpiresAt) VALUES (@Id, @Name, @Owner, @CreatedAt, @ExpiresAt)",
            new { Id = "act0000002", Name = "still-active", Owner = "admin", CreatedAt = DateTime.UtcNow, ExpiresAt = DateTime.UtcNow.AddDays(30) });

        await _sut.CleanupExpiredBucketsAsync(TestContext.Current.CancellationToken);

        _keepAliveConnection.ExecuteScalar<int>("SELECT COUNT(*) FROM Buckets").Should().Be(1);
    }

    [Fact]
    public async Task CleanupExpiredBucketsAsync_OnlyRemovesExpired_LeavesActive()
    {
        _keepAliveConnection.Execute(
            "INSERT INTO Buckets (Id, Name, Owner, CreatedAt, ExpiresAt) VALUES (@Id, @Name, @Owner, @CreatedAt, @ExpiresAt)",
            new { Id = "exp0000006", Name = "expired", Owner = "admin", CreatedAt = DateTime.UtcNow.AddDays(-10), ExpiresAt = DateTime.UtcNow.AddDays(-1) });
        _keepAliveConnection.Execute(
            "INSERT INTO Buckets (Id, Name, Owner, CreatedAt, ExpiresAt) VALUES (@Id, @Name, @Owner, @CreatedAt, @ExpiresAt)",
            new { Id = "act0000003", Name = "active", Owner = "admin", CreatedAt = DateTime.UtcNow, ExpiresAt = DateTime.UtcNow.AddDays(7) });

        await _sut.CleanupExpiredBucketsAsync(TestContext.Current.CancellationToken);

        _keepAliveConnection.ExecuteScalar<int>("SELECT COUNT(*) FROM Buckets WHERE Id = 'exp0000006'").Should().Be(0);
        _keepAliveConnection.ExecuteScalar<int>("SELECT COUNT(*) FROM Buckets WHERE Id = 'act0000003'").Should().Be(1);
    }

    [Fact]
    public async Task CleanupExpiredBucketsAsync_RemovesMultipleExpiredBuckets()
    {
        _keepAliveConnection.Execute(
            "INSERT INTO Buckets (Id, Name, Owner, CreatedAt, ExpiresAt) VALUES (@Id, @Name, @Owner, @CreatedAt, @ExpiresAt)",
            new { Id = "exp0000007", Name = "expired-1", Owner = "admin", CreatedAt = DateTime.UtcNow.AddDays(-10), ExpiresAt = DateTime.UtcNow.AddDays(-2) });
        _keepAliveConnection.Execute(
            "INSERT INTO Buckets (Id, Name, Owner, CreatedAt, ExpiresAt) VALUES (@Id, @Name, @Owner, @CreatedAt, @ExpiresAt)",
            new { Id = "exp0000008", Name = "expired-2", Owner = "admin", CreatedAt = DateTime.UtcNow.AddDays(-5), ExpiresAt = DateTime.UtcNow.AddDays(-1) });

        await _sut.CleanupExpiredBucketsAsync(TestContext.Current.CancellationToken);

        _keepAliveConnection.ExecuteScalar<int>("SELECT COUNT(*) FROM Buckets").Should().Be(0);
    }

    [Fact]
    public async Task CleanupExpiredBucketsAsync_FullCleanup_RemovesAllAssociatedData()
    {
        // Create an expired bucket with files, short URLs, upload tokens, and a directory
        var bucketDir = Path.Combine(_tempDir, "exp0000009");
        Directory.CreateDirectory(bucketDir);
        File.WriteAllText(Path.Combine(bucketDir, "data.bin"), "test data");

        _keepAliveConnection.Execute(
            "INSERT INTO Buckets (Id, Name, Owner, CreatedAt, ExpiresAt, FileCount, TotalSize) VALUES (@Id, @Name, @Owner, @CreatedAt, @ExpiresAt, @FileCount, @TotalSize)",
            new { Id = "exp0000009", Name = "full-cleanup", Owner = "admin", CreatedAt = DateTime.UtcNow.AddDays(-10), ExpiresAt = DateTime.UtcNow.AddDays(-1), FileCount = 1, TotalSize = 100L });
        _keepAliveConnection.Execute(
            "INSERT INTO Files (BucketId, Path, Name, Size, MimeType, CreatedAt, UpdatedAt) VALUES (@BucketId, @Path, @Name, @Size, @MimeType, @CreatedAt, @UpdatedAt)",
            new { BucketId = "exp0000009", Path = "data.bin", Name = "data.bin", Size = 100L, MimeType = "application/octet-stream", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow });
        _keepAliveConnection.Execute(
            "INSERT INTO ShortUrls (Code, BucketId, FilePath, CreatedAt) VALUES (@Code, @BucketId, @FilePath, @CreatedAt)",
            new { Code = "xyz789", BucketId = "exp0000009", FilePath = "data.bin", CreatedAt = DateTime.UtcNow });
        _keepAliveConnection.Execute(
            "INSERT INTO UploadTokens (Token, BucketId, ExpiresAt, CreatedAt) VALUES (@Token, @BucketId, @ExpiresAt, @CreatedAt)",
            new { Token = "cfu_fullcleanup12345678901234567890123456789012", BucketId = "exp0000009", ExpiresAt = DateTime.UtcNow.AddDays(1), CreatedAt = DateTime.UtcNow });

        await _sut.CleanupExpiredBucketsAsync(TestContext.Current.CancellationToken);

        // All database records should be gone
        _keepAliveConnection.ExecuteScalar<int>("SELECT COUNT(*) FROM Buckets WHERE Id = 'exp0000009'").Should().Be(0);
        _keepAliveConnection.ExecuteScalar<int>("SELECT COUNT(*) FROM Files WHERE BucketId = 'exp0000009'").Should().Be(0);
        _keepAliveConnection.ExecuteScalar<int>("SELECT COUNT(*) FROM ShortUrls WHERE BucketId = 'exp0000009'").Should().Be(0);
        _keepAliveConnection.ExecuteScalar<int>("SELECT COUNT(*) FROM UploadTokens WHERE BucketId = 'exp0000009'").Should().Be(0);

        // Directory should be gone
        Directory.Exists(bucketDir).Should().BeFalse();
    }
}
