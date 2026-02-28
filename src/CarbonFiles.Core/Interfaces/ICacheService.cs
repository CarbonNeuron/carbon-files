using CarbonFiles.Core.Models;
using CarbonFiles.Core.Models.Responses;

namespace CarbonFiles.Core.Interfaces;

public interface ICacheService
{
    // Bucket cache
    BucketDetailResponse? GetBucket(string id);
    void SetBucket(string id, BucketDetailResponse bucket);
    void InvalidateBucket(string id);

    // File metadata cache
    BucketFile? GetFileMetadata(string bucketId, string path);
    void SetFileMetadata(string bucketId, string path, BucketFile file);
    void InvalidateFile(string bucketId, string path);
    void InvalidateFilesForBucket(string bucketId);

    // Short URL cache
    (string BucketId, string FilePath)? GetShortUrl(string code);
    void SetShortUrl(string code, string bucketId, string filePath);
    void InvalidateShortUrl(string code);
    void InvalidateShortUrlsForBucket(string bucketId);

    // Upload token cache
    (string BucketId, bool IsValid)? GetUploadToken(string token);
    void SetUploadToken(string token, string bucketId, bool isValid);
    void InvalidateUploadToken(string token);
    void InvalidateUploadTokensForBucket(string bucketId);

    // Stats cache
    StatsResponse? GetStats();
    void SetStats(StatsResponse stats);
    void InvalidateStats();
}
