using System.Data;
using CarbonFiles.Core.Interfaces;
using CarbonFiles.Core.Models;
using CarbonFiles.Core.Models.Requests;
using CarbonFiles.Core.Models.Responses;
using CarbonFiles.Core.Utilities;
using CarbonFiles.Infrastructure.Data.Entities;
using Dapper;
using Microsoft.Extensions.Logging;

namespace CarbonFiles.Infrastructure.Services;

public sealed class UploadTokenService : IUploadTokenService
{
    private readonly IDbConnection _db;
    private readonly ICacheService _cache;
    private readonly ILogger<UploadTokenService> _logger;

    public UploadTokenService(IDbConnection db, ICacheService cache, ILogger<UploadTokenService> logger)
    {
        _db = db;
        _cache = cache;
        _logger = logger;
    }

    public async Task<UploadTokenResponse> CreateAsync(string bucketId, CreateUploadTokenRequest request, AuthContext auth)
    {
        // Verify bucket exists
        var bucket = await _db.QueryFirstOrDefaultAsync<BucketEntity>(
            "SELECT * FROM Buckets WHERE Id = @bucketId", new { bucketId });
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

        await _db.ExecuteAsync(
            "INSERT INTO UploadTokens (Token, BucketId, ExpiresAt, MaxUploads, UploadsUsed, CreatedAt) VALUES (@Token, @BucketId, @ExpiresAt, @MaxUploads, @UploadsUsed, @CreatedAt)",
            entity);

        _cache.InvalidateStats();
        _logger.LogInformation("Created upload token for bucket {BucketId} (expires {ExpiresAt}, max uploads {MaxUploads})",
            bucketId, entity.ExpiresAt.ToString("o"), request.MaxUploads?.ToString() ?? "unlimited");

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
        var cached = _cache.GetUploadToken(token);
        if (cached != null)
            return cached.Value;

        var entity = await _db.QueryFirstOrDefaultAsync<UploadTokenEntity>(
            "SELECT * FROM UploadTokens WHERE Token = @token", new { token });
        if (entity == null)
        {
            _logger.LogDebug("Upload token not found");
            return (string.Empty, false);
        }

        // Check not expired
        if (entity.ExpiresAt <= DateTime.UtcNow)
        {
            _logger.LogDebug("Upload token for bucket {BucketId} is expired", entity.BucketId);
            _cache.SetUploadToken(token, entity.BucketId, false);
            return (entity.BucketId, false);
        }

        // Check uploads_used < max_uploads (if max_uploads set)
        if (entity.MaxUploads.HasValue && entity.UploadsUsed >= entity.MaxUploads.Value)
        {
            _logger.LogDebug("Upload token for bucket {BucketId} exhausted ({Used}/{Max})", entity.BucketId, entity.UploadsUsed, entity.MaxUploads.Value);
            _cache.SetUploadToken(token, entity.BucketId, false);
            return (entity.BucketId, false);
        }

        _cache.SetUploadToken(token, entity.BucketId, true);
        return (entity.BucketId, true);
    }

    public async Task IncrementUsageAsync(string token, int count)
    {
        // Atomically increment uploads_used
        await _db.ExecuteAsync(
            "UPDATE UploadTokens SET UploadsUsed = UploadsUsed + @count WHERE Token = @token",
            new { count, token });

        _cache.InvalidateUploadToken(token);
    }
}
