using System.Data;
using CarbonFiles.Infrastructure.Data.Entities;
using Dapper;

namespace CarbonFiles.Infrastructure.Services;

public sealed class CleanupRepository
{
    private readonly IDbConnection _db;

    public CleanupRepository(IDbConnection db)
    {
        _db = db;
    }

    public async Task<List<BucketEntity>> GetExpiredBucketsAsync(DateTime now, CancellationToken ct)
    {
        return (await _db.QueryAsync<BucketEntity>(
            new CommandDefinition(
                "SELECT * FROM Buckets WHERE ExpiresAt IS NOT NULL AND ExpiresAt < @now",
                new { now },
                cancellationToken: ct))).AsList();
    }

    public async Task DeleteBucketAndRelatedAsync(string bucketId, CancellationToken ct)
    {
        using var tx = _db.BeginTransaction();

        await _db.ExecuteAsync(new CommandDefinition(
            "DELETE FROM Files WHERE BucketId = @bucketId", new { bucketId }, tx, cancellationToken: ct));
        await _db.ExecuteAsync(new CommandDefinition(
            "DELETE FROM ShortUrls WHERE BucketId = @bucketId", new { bucketId }, tx, cancellationToken: ct));
        await _db.ExecuteAsync(new CommandDefinition(
            "DELETE FROM UploadTokens WHERE BucketId = @bucketId", new { bucketId }, tx, cancellationToken: ct));
        await _db.ExecuteAsync(new CommandDefinition(
            "DELETE FROM Buckets WHERE Id = @bucketId", new { bucketId }, tx, cancellationToken: ct));

        tx.Commit();
    }
}
