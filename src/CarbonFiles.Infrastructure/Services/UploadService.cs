using CarbonFiles.Core.Interfaces;
using CarbonFiles.Core.Models;
using CarbonFiles.Core.Utilities;
using CarbonFiles.Infrastructure.Data;
using CarbonFiles.Infrastructure.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace CarbonFiles.Infrastructure.Services;

public sealed class UploadService : IUploadService
{
    private readonly CarbonFilesDbContext _db;
    private readonly FileStorageService _storage;
    private readonly INotificationService _notifications;
    private readonly ICacheService _cache;
    private readonly ILogger<UploadService> _logger;

    public UploadService(CarbonFilesDbContext db, FileStorageService storage, INotificationService notifications, ICacheService cache, ILogger<UploadService> logger)
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
        var existing = await _db.Files.FirstOrDefaultAsync(f => f.BucketId == bucketId && f.Path == normalized);
        var now = DateTime.UtcNow;

        if (existing != null)
        {
            // Update existing file
            var oldSize = existing.Size;
            existing.Size = size;
            existing.MimeType = mimeType;
            existing.Name = name;
            existing.UpdatedAt = now;

            // Update bucket total size (difference)
            var bucket = await _db.Buckets.FirstOrDefaultAsync(b => b.Id == bucketId);
            if (bucket != null)
            {
                bucket.TotalSize = Math.Max(0, bucket.TotalSize - oldSize) + size;
                bucket.LastUsedAt = now;
            }

            await _db.SaveChangesAsync();
            _cache.InvalidateFile(bucketId, normalized);
            _cache.InvalidateBucket(bucketId);
            _cache.InvalidateStats();

            _logger.LogInformation("Updated file {Path} in bucket {BucketId} ({OldSize} -> {Size} bytes)", normalized, bucketId, oldSize, size);

            var updatedFile = new BucketFile
            {
                Path = existing.Path,
                Name = existing.Name,
                Size = existing.Size,
                MimeType = existing.MimeType,
                ShortCode = existing.ShortCode,
                ShortUrl = existing.ShortCode != null ? $"/s/{existing.ShortCode}" : null,
                CreatedAt = existing.CreatedAt,
                UpdatedAt = existing.UpdatedAt
            };

            await _notifications.NotifyFileUpdated(bucketId, updatedFile);
            return updatedFile;
        }
        else
        {
            // Create new file
            var shortCode = IdGenerator.GenerateShortCode();

            var entity = new FileEntity
            {
                BucketId = bucketId,
                Path = normalized,
                Name = name,
                Size = size,
                MimeType = mimeType,
                ShortCode = shortCode,
                CreatedAt = now,
                UpdatedAt = now
            };

            _db.Files.Add(entity);

            // Create short URL
            var shortUrl = new ShortUrlEntity
            {
                Code = shortCode,
                BucketId = bucketId,
                FilePath = normalized,
                CreatedAt = now
            };
            _db.ShortUrls.Add(shortUrl);

            // Update bucket stats
            var bucket = await _db.Buckets.FirstOrDefaultAsync(b => b.Id == bucketId);
            if (bucket != null)
            {
                bucket.FileCount++;
                bucket.TotalSize += size;
                bucket.LastUsedAt = now;
            }

            await _db.SaveChangesAsync();
            _cache.InvalidateFile(bucketId, normalized);
            _cache.InvalidateBucket(bucketId);
            _cache.InvalidateStats();

            _logger.LogInformation("Created file {Path} in bucket {BucketId} ({Size} bytes, short code {ShortCode})", normalized, bucketId, size, shortCode);

            var createdFile = new BucketFile
            {
                Path = entity.Path,
                Name = entity.Name,
                Size = entity.Size,
                MimeType = entity.MimeType,
                ShortCode = entity.ShortCode,
                ShortUrl = $"/s/{entity.ShortCode}",
                CreatedAt = entity.CreatedAt,
                UpdatedAt = entity.UpdatedAt
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
