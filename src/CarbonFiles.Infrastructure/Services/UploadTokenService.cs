using CarbonFiles.Core.Interfaces;
using CarbonFiles.Core.Models;
using CarbonFiles.Core.Models.Requests;
using CarbonFiles.Core.Models.Responses;
using CarbonFiles.Core.Utilities;
using CarbonFiles.Infrastructure.Data;
using CarbonFiles.Infrastructure.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace CarbonFiles.Infrastructure.Services;

public sealed class UploadTokenService : IUploadTokenService
{
    private readonly CarbonFilesDbContext _db;

    public UploadTokenService(CarbonFilesDbContext db)
    {
        _db = db;
    }

    public async Task<UploadTokenResponse> CreateAsync(string bucketId, CreateUploadTokenRequest request, AuthContext auth)
    {
        // Verify bucket exists
        var bucket = await _db.Buckets.FirstOrDefaultAsync(b => b.Id == bucketId);
        if (bucket == null)
            return null!;

        // Verify auth can manage this bucket
        if (!auth.CanManage(bucket.Owner))
            return null!;

        var token = IdGenerator.GenerateUploadToken();

        // Parse expires_in with default of 1 day
        var expiresAt = ExpiryParser.Parse(request.ExpiresIn, DateTime.UtcNow.AddDays(1));

        var entity = new UploadTokenEntity
        {
            Token = token,
            BucketId = bucketId,
            ExpiresAt = expiresAt ?? DateTime.UtcNow.AddDays(1),
            MaxUploads = request.MaxUploads,
            UploadsUsed = 0,
            CreatedAt = DateTime.UtcNow
        };

        _db.UploadTokens.Add(entity);
        await _db.SaveChangesAsync();

        return new UploadTokenResponse
        {
            Token = token,
            BucketId = bucketId,
            ExpiresAt = entity.ExpiresAt,
            MaxUploads = entity.MaxUploads,
            UploadsUsed = 0
        };
    }

    public async Task<(string BucketId, bool IsValid)> ValidateAsync(string token)
    {
        var entity = await _db.UploadTokens.FirstOrDefaultAsync(t => t.Token == token);
        if (entity == null)
            return (string.Empty, false);

        // Check not expired
        if (entity.ExpiresAt <= DateTime.UtcNow)
            return (entity.BucketId, false);

        // Check uploads_used < max_uploads (if max_uploads set)
        if (entity.MaxUploads.HasValue && entity.UploadsUsed >= entity.MaxUploads.Value)
            return (entity.BucketId, false);

        return (entity.BucketId, true);
    }

    public async Task IncrementUsageAsync(string token, int count)
    {
        // Atomically increment uploads_used
        await _db.UploadTokens
            .Where(t => t.Token == token)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.UploadsUsed, t => t.UploadsUsed + count));
    }
}
