using System.Data;
using CarbonFiles.Core.Interfaces;
using CarbonFiles.Core.Models;
using CarbonFiles.Core.Utilities;
using CarbonFiles.Infrastructure.Data.Entities;
using Dapper;
using Microsoft.Extensions.Logging;

namespace CarbonFiles.Infrastructure.Services;

public sealed class UploadService : IUploadService
{
    private readonly IDbConnection _db;
    private readonly FileStorageService _storage;
    private readonly INotificationService _notifications;
    private readonly ICacheService _cache;
    private readonly ILogger<UploadService> _logger;

    public UploadService(IDbConnection db, FileStorageService storage, INotificationService notifications, ICacheService cache, ILogger<UploadService> logger)
    {
        _db = db;
        _storage = storage;
        _notifications = notifications;
        _cache = cache;
        _logger = logger;
    }

    public async Task<BucketFile> StoreFileAsync(string bucketId, string path, Stream content, AuthContext auth)
    {
        _logger.LogDebug("Storing file {Path} in bucket {BucketId}", path, bucketId);

        var normalized = path.ToLowerInvariant();
        var name = Path.GetFileName(path);
        var mimeType = MimeDetector.DetectFromExtension(path);

        // Stream content to disk
        var size = await _storage.StoreAsync(bucketId, normalized, content);

        // Check if file already exists
        var existing = await _db.QueryFirstOrDefaultAsync<FileEntity>(
            "SELECT * FROM Files WHERE BucketId = @bucketId AND Path = @normalized",
            new { bucketId, normalized });
        var now = DateTime.UtcNow;

        if (existing != null)
        {
            // Update existing file
            var oldSize = existing.Size;

            using var tx = _db.BeginTransaction();

            await _db.ExecuteAsync(
                "UPDATE Files SET Size = @size, MimeType = @mimeType, Name = @name, UpdatedAt = @now WHERE BucketId = @bucketId AND Path = @normalized",
                new { size, mimeType, name, now, bucketId, normalized }, tx);

            // Update bucket total size (difference) and last used
            await _db.ExecuteAsync(
                "UPDATE Buckets SET TotalSize = MAX(0, TotalSize - @oldSize) + @size, LastUsedAt = @now WHERE Id = @bucketId",
                new { oldSize, size, now, bucketId }, tx);

            tx.Commit();

            _cache.InvalidateFile(bucketId, normalized);
            _cache.InvalidateBucket(bucketId);
            _cache.InvalidateStats();

            _logger.LogInformation("Updated file {Path} in bucket {BucketId} ({OldSize} -> {Size} bytes)", normalized, bucketId, oldSize, size);

            var updatedFile = new BucketFile
            {
                Path = existing.Path,
                Name = name,
                Size = size,
                MimeType = mimeType,
                ShortCode = existing.ShortCode,
                ShortUrl = existing.ShortCode != null ? $"/s/{existing.ShortCode}" : null,
                CreatedAt = existing.CreatedAt,
                UpdatedAt = now
            };

            await _notifications.NotifyFileUpdated(bucketId, updatedFile);
            return updatedFile;
        }
        else
        {
            // Create new file
            var shortCode = IdGenerator.GenerateShortCode();

            using var tx = _db.BeginTransaction();

            await _db.ExecuteAsync(
                "INSERT INTO Files (BucketId, Path, Name, Size, MimeType, ShortCode, CreatedAt, UpdatedAt) VALUES (@BucketId, @Path, @Name, @Size, @MimeType, @ShortCode, @CreatedAt, @UpdatedAt)",
                new { BucketId = bucketId, Path = normalized, Name = name, Size = size, MimeType = mimeType, ShortCode = shortCode, CreatedAt = now, UpdatedAt = now }, tx);

            // Create short URL
            await _db.ExecuteAsync(
                "INSERT INTO ShortUrls (Code, BucketId, FilePath, CreatedAt) VALUES (@Code, @BucketId, @FilePath, @CreatedAt)",
                new { Code = shortCode, BucketId = bucketId, FilePath = normalized, CreatedAt = now }, tx);

            // Update bucket stats
            await _db.ExecuteAsync(
                "UPDATE Buckets SET FileCount = FileCount + 1, TotalSize = TotalSize + @size, LastUsedAt = @now WHERE Id = @bucketId",
                new { size, now, bucketId }, tx);

            tx.Commit();

            _cache.InvalidateFile(bucketId, normalized);
            _cache.InvalidateBucket(bucketId);
            _cache.InvalidateStats();

            _logger.LogInformation("Created file {Path} in bucket {BucketId} ({Size} bytes, short code {ShortCode})", normalized, bucketId, size, shortCode);

            var createdFile = new BucketFile
            {
                Path = normalized,
                Name = name,
                Size = size,
                MimeType = mimeType,
                ShortCode = shortCode,
                ShortUrl = $"/s/{shortCode}",
                CreatedAt = now,
                UpdatedAt = now
            };

            await _notifications.NotifyFileCreated(bucketId, createdFile);
            return createdFile;
        }
    }

    public Task<long> GetStoredFileSizeAsync(string bucketId, string path)
    {
        var size = _storage.GetFileSize(bucketId, path.ToLowerInvariant());
        return Task.FromResult(size);
    }
}
