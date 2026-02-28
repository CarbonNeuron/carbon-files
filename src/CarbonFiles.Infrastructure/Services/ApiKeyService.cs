using System.Security.Cryptography;
using System.Text;
using CarbonFiles.Core.Interfaces;
using CarbonFiles.Core.Models;
using CarbonFiles.Core.Models.Responses;
using CarbonFiles.Core.Utilities;
using CarbonFiles.Infrastructure.Data;
using CarbonFiles.Infrastructure.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace CarbonFiles.Infrastructure.Services;

public sealed class ApiKeyService : IApiKeyService
{
    private readonly CarbonFilesDbContext _db;

    public ApiKeyService(CarbonFilesDbContext db) => _db = db;

    public async Task<ApiKeyResponse> CreateAsync(string name)
    {
        var (fullKey, prefix) = IdGenerator.GenerateApiKey();
        // fullKey = "cf4_{8hex}_{32hex}", prefix = "cf4_{8hex}"
        // Extract secret: everything after the prefix and the underscore separator
        var secret = fullKey[(prefix.Length + 1)..];
        var hashed = HashSecret(secret);

        var entity = new ApiKeyEntity
        {
            Prefix = prefix,
            HashedSecret = hashed,
            Name = name,
            CreatedAt = DateTime.UtcNow
        };
        _db.ApiKeys.Add(entity);
        await _db.SaveChangesAsync();

        return new ApiKeyResponse
        {
            Key = fullKey,
            Prefix = prefix,
            Name = name,
            CreatedAt = entity.CreatedAt
        };
    }

    public async Task<PaginatedResponse<ApiKeyListItem>> ListAsync(PaginationParams pagination)
    {
        var total = await _db.ApiKeys.CountAsync();

        // Build base query with usage stats from buckets
        var query = from k in _db.ApiKeys
                    select new
                    {
                        Key = k,
                        BucketCount = _db.Buckets.Count(b => b.OwnerKeyPrefix == k.Prefix),
                        FileCount = _db.Buckets.Where(b => b.OwnerKeyPrefix == k.Prefix).Sum(b => b.FileCount),
                        TotalSize = _db.Buckets.Where(b => b.OwnerKeyPrefix == k.Prefix).Sum(b => b.TotalSize)
                    };

        // Apply sorting
        query = (pagination.Sort?.ToLowerInvariant(), pagination.Order?.ToLowerInvariant()) switch
        {
            ("name", "asc") => query.OrderBy(x => x.Key.Name),
            ("name", _) => query.OrderByDescending(x => x.Key.Name),
            ("last_used_at", "asc") => query.OrderBy(x => x.Key.LastUsedAt),
            ("last_used_at", _) => query.OrderByDescending(x => x.Key.LastUsedAt),
            ("total_size", "asc") => query.OrderBy(x => x.TotalSize),
            ("total_size", _) => query.OrderByDescending(x => x.TotalSize),
            ("created_at", "asc") => query.OrderBy(x => x.Key.CreatedAt),
            _ => query.OrderByDescending(x => x.Key.CreatedAt), // default: created_at desc
        };

        var items = await query
            .Skip(pagination.Offset)
            .Take(pagination.Limit)
            .Select(x => new ApiKeyListItem
            {
                Prefix = x.Key.Prefix,
                Name = x.Key.Name,
                CreatedAt = x.Key.CreatedAt,
                LastUsedAt = x.Key.LastUsedAt,
                BucketCount = x.BucketCount,
                FileCount = x.FileCount,
                TotalSize = x.TotalSize
            })
            .ToListAsync();

        return new PaginatedResponse<ApiKeyListItem>
        {
            Items = items,
            Total = total,
            Limit = pagination.Limit,
            Offset = pagination.Offset
        };
    }

    public async Task<bool> DeleteAsync(string prefix)
    {
        var entity = await _db.ApiKeys.FindAsync(prefix);
        if (entity == null)
            return false;

        _db.ApiKeys.Remove(entity);
        await _db.SaveChangesAsync();
        return true;
    }

    public async Task<ApiKeyUsageResponse?> GetUsageAsync(string prefix)
    {
        var entity = await _db.ApiKeys.FirstOrDefaultAsync(k => k.Prefix == prefix);
        if (entity == null)
            return null;

        var buckets = await _db.Buckets
            .Where(b => b.OwnerKeyPrefix == prefix)
            .Select(b => new Bucket
            {
                Id = b.Id,
                Name = b.Name,
                Owner = b.Owner,
                Description = b.Description,
                CreatedAt = b.CreatedAt,
                ExpiresAt = b.ExpiresAt,
                LastUsedAt = b.LastUsedAt,
                FileCount = b.FileCount,
                TotalSize = b.TotalSize
            })
            .ToListAsync();

        var totalFiles = buckets.Sum(b => b.FileCount);
        var totalSize = buckets.Sum(b => b.TotalSize);
        var totalDownloads = await _db.Buckets
            .Where(b => b.OwnerKeyPrefix == prefix)
            .SumAsync(b => b.DownloadCount);

        return new ApiKeyUsageResponse
        {
            Prefix = entity.Prefix,
            Name = entity.Name,
            CreatedAt = entity.CreatedAt,
            LastUsedAt = entity.LastUsedAt,
            BucketCount = buckets.Count,
            FileCount = totalFiles,
            TotalSize = totalSize,
            TotalDownloads = totalDownloads,
            Buckets = buckets
        };
    }

    public async Task<(string Name, string Prefix)?> ValidateKeyAsync(string fullKey)
    {
        // Key format: cf4_{8hex}_{32hex}
        var parts = fullKey.Split('_', 3);
        if (parts.Length != 3 || parts[0] != "cf4")
            return null;

        var prefix = $"cf4_{parts[1]}";
        var secret = parts[2];

        var entity = await _db.ApiKeys.FirstOrDefaultAsync(k => k.Prefix == prefix);
        if (entity == null)
            return null;

        var hashed = HashSecret(secret);
        if (hashed != entity.HashedSecret)
            return null;

        return (entity.Name, entity.Prefix);
    }

    private static string HashSecret(string secret)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(secret));
        return Convert.ToHexStringLower(bytes);
    }
}
