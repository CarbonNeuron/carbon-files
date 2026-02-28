using CarbonFiles.Core.Interfaces;
using CarbonFiles.Core.Models;
using CarbonFiles.Core.Utilities;
using CarbonFiles.Infrastructure.Data;
using CarbonFiles.Infrastructure.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace CarbonFiles.Infrastructure.Services;

public sealed class ShortUrlService : IShortUrlService
{
    private readonly CarbonFilesDbContext _db;

    public ShortUrlService(CarbonFilesDbContext db)
    {
        _db = db;
    }

    public async Task<string> CreateAsync(string bucketId, string filePath)
    {
        const int maxAttempts = 5;

        for (int attempt = 0; attempt < maxAttempts; attempt++)
        {
            var code = IdGenerator.GenerateShortCode();

            var exists = await _db.ShortUrls.AnyAsync(s => s.Code == code);
            if (exists)
                continue;

            var entity = new ShortUrlEntity
            {
                Code = code,
                BucketId = bucketId,
                FilePath = filePath,
                CreatedAt = DateTime.UtcNow
            };

            _db.ShortUrls.Add(entity);
            await _db.SaveChangesAsync();

            return code;
        }

        throw new InvalidOperationException("Failed to generate a unique short code after maximum attempts.");
    }

    public async Task<string?> ResolveAsync(string code)
    {
        var shortUrl = await _db.ShortUrls.FirstOrDefaultAsync(s => s.Code == code);
        if (shortUrl == null)
            return null;

        // Verify the associated bucket hasn't expired
        var bucket = await _db.Buckets.FirstOrDefaultAsync(b => b.Id == shortUrl.BucketId);
        if (bucket == null)
            return null;

        if (bucket.ExpiresAt.HasValue && bucket.ExpiresAt.Value < DateTime.UtcNow)
            return null;

        return $"/api/buckets/{shortUrl.BucketId}/files/{shortUrl.FilePath}/content";
    }

    public async Task<bool> DeleteAsync(string code, AuthContext auth)
    {
        var shortUrl = await _db.ShortUrls.FirstOrDefaultAsync(s => s.Code == code);
        if (shortUrl == null)
            return false;

        // Find the bucket owner and verify auth can manage
        var bucket = await _db.Buckets.FirstOrDefaultAsync(b => b.Id == shortUrl.BucketId);
        if (bucket == null)
            return false;

        if (!auth.CanManage(bucket.Owner))
            return false;

        _db.ShortUrls.Remove(shortUrl);
        await _db.SaveChangesAsync();

        return true;
    }
}
