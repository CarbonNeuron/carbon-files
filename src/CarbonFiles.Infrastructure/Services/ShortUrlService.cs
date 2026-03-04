using System.Data;
using CarbonFiles.Core.Interfaces;
using CarbonFiles.Core.Models;
using CarbonFiles.Core.Utilities;
using CarbonFiles.Infrastructure.Data.Entities;
using Dapper;
using Microsoft.Extensions.Logging;

namespace CarbonFiles.Infrastructure.Services;

public sealed class ShortUrlService : IShortUrlService
{
    private readonly IDbConnection _db;
    private readonly ICacheService _cache;
    private readonly ILogger<ShortUrlService> _logger;

    public ShortUrlService(IDbConnection db, ICacheService cache, ILogger<ShortUrlService> logger)
    {
        _db = db;
        _cache = cache;
        _logger = logger;
    }

    public async Task<string> CreateAsync(string bucketId, string filePath)
    {
        const int maxAttempts = 5;

        for (int attempt = 0; attempt < maxAttempts; attempt++)
        {
            var code = IdGenerator.GenerateShortCode();

            var exists = await _db.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM ShortUrls WHERE Code = @code", new { code }) > 0;
            if (exists)
            {
                _logger.LogDebug("Short code collision, retrying (attempt {Attempt})", attempt + 1);
                continue;
            }

            var now = DateTime.UtcNow;
            await _db.ExecuteAsync(
                "INSERT INTO ShortUrls (Code, BucketId, FilePath, CreatedAt) VALUES (@code, @bucketId, @filePath, @now)",
                new { code, bucketId, filePath, now });

            _cache.SetShortUrl(code, bucketId, filePath);
            _logger.LogInformation("Created short URL {Code} for bucket {BucketId} file {FilePath}", code, bucketId, filePath);

            return code;
        }

        throw new InvalidOperationException("Failed to generate a unique short code after maximum attempts.");
    }

    public async Task<string?> ResolveAsync(string code)
    {
        var cached = _cache.GetShortUrl(code);
        if (cached != null)
            return $"/api/buckets/{cached.Value.BucketId}/files/{cached.Value.FilePath}/content";

        var shortUrl = await _db.QueryFirstOrDefaultAsync<ShortUrlEntity>(
            "SELECT Code, BucketId, FilePath, CreatedAt FROM ShortUrls WHERE Code = @code", new { code });
        if (shortUrl == null)
        {
            _logger.LogDebug("Short URL {Code} not found", code);
            return null;
        }

        // Verify the associated bucket hasn't expired
        var bucket = await _db.QueryFirstOrDefaultAsync<BucketEntity>(
            "SELECT * FROM Buckets WHERE Id = @Id", new { Id = shortUrl.BucketId });
        if (bucket == null)
            return null;

        if (bucket.ExpiresAt.HasValue && bucket.ExpiresAt.Value < DateTime.UtcNow)
        {
            _logger.LogDebug("Short URL {Code} points to expired bucket {BucketId}", code, shortUrl.BucketId);
            return null;
        }

        _cache.SetShortUrl(code, shortUrl.BucketId, shortUrl.FilePath);
        return $"/api/buckets/{shortUrl.BucketId}/files/{shortUrl.FilePath}/content";
    }

    public async Task<bool> DeleteAsync(string code, AuthContext auth)
    {
        var shortUrl = await _db.QueryFirstOrDefaultAsync<ShortUrlEntity>(
            "SELECT Code, BucketId, FilePath, CreatedAt FROM ShortUrls WHERE Code = @code", new { code });
        if (shortUrl == null)
            return false;

        // Find the bucket owner and verify auth can manage
        var bucket = await _db.QueryFirstOrDefaultAsync<BucketEntity>(
            "SELECT * FROM Buckets WHERE Id = @Id", new { Id = shortUrl.BucketId });
        if (bucket == null)
            return false;

        if (!auth.CanManage(bucket.Owner))
            return false;

        await _db.ExecuteAsync("DELETE FROM ShortUrls WHERE Code = @code", new { code });

        _logger.LogInformation("Deleted short URL {Code}", code);

        _cache.InvalidateShortUrl(code);
        return true;
    }
}
