using System.Data;
using CarbonFiles.Core.Interfaces;
using CarbonFiles.Core.Models.Responses;
using Dapper;

namespace CarbonFiles.Infrastructure.Services;

public sealed class StatsService : IStatsService
{
    private readonly IDbConnection _db;

    public StatsService(IDbConnection db)
    {
        _db = db;
    }

    public async Task<StatsResponse> GetStatsAsync(CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;

        var totalBuckets = await _db.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM Buckets WHERE ExpiresAt IS NULL OR ExpiresAt > @now",
            new { now });

        var totalFiles = await _db.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM Files");

        var totalSize = await _db.ExecuteScalarAsync<long?>(
            "SELECT SUM(Size) FROM Files") ?? 0;

        var totalKeys = await _db.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM ApiKeys");

        var totalDownloads = await _db.ExecuteScalarAsync<long?>(
            "SELECT SUM(DownloadCount) FROM Buckets WHERE ExpiresAt IS NULL OR ExpiresAt > @now",
            new { now }) ?? 0;

        var storageByOwner = (await _db.QueryAsync<OwnerStats>(
            """
            SELECT Owner, COUNT(*) AS BucketCount, SUM(FileCount) AS FileCount, SUM(TotalSize) AS TotalSize
            FROM Buckets
            WHERE ExpiresAt IS NULL OR ExpiresAt > @now
            GROUP BY Owner
            """,
            new { now })).AsList();

        return new StatsResponse
        {
            TotalBuckets = totalBuckets,
            TotalFiles = totalFiles,
            TotalSize = totalSize,
            TotalKeys = totalKeys,
            TotalDownloads = totalDownloads,
            StorageByOwner = storageByOwner
        };
    }
}
