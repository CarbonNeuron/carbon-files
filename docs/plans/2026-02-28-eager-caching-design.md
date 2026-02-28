# Eager & Optimistic Caching Design

## Overview

Centralized `CacheService` backed by `IMemoryCache` with eager invalidation on all mutations. Covers bucket metadata, file metadata, short URL resolution, upload token validation, and admin stats. Zero stale reads.

## Current State

Only AuthService has a 30s IMemoryCache for API key validation. All other data access goes directly to SQLite on every request. Typical request makes 2-4 DB round-trips.

## Design

### ICacheService Interface

Typed methods organized by entity:

- **Bucket**: `GetBucket(id)`, `SetBucket(id, entity)`, `InvalidateBucket(id)`
- **File**: `GetFileMetadata(bucketId, path)`, `SetFileMetadata(bucketId, path, file)`, `InvalidateFile(bucketId, path)`, `InvalidateFilesForBucket(bucketId)`
- **Short URL**: `GetShortUrl(code)`, `SetShortUrl(code, bucketId, filePath)`, `InvalidateShortUrl(code)`, `InvalidateShortUrlsForBucket(bucketId)`
- **Upload Token**: `GetUploadToken(token)`, `SetUploadToken(token, data)`, `InvalidateUploadToken(token)`, `InvalidateUploadTokensForBucket(bucketId)`
- **Stats**: `GetStats()`, `SetStats(stats)`, `InvalidateStats()`

### Cache Key Format

```
bucket:{id}
file:{bucketId}:{path}
short:{code}
uploadtoken:{token}
stats
```

### TTLs (safety nets)

Buckets 10min, files 5min, short URLs 10min, upload tokens 2min, stats 5min.

### Bucket-Scoped Bulk Invalidation

Track cache keys per bucket using `ConcurrentDictionary<string, HashSet<string>>`. On bucket delete, iterate and remove all tracked keys.

### Service Integration

| Service | Cache Read | Cache Write | Invalidate |
|---------|-----------|-------------|------------|
| BucketService | GetByIdAsync | After create/get | On update, delete |
| FileService | GetMetadataAsync | After fetch | On delete, size update |
| UploadService | — | After create/update | File + bucket |
| ShortUrlService | ResolveAsync | After resolution | On delete |
| UploadTokenService | ValidateAsync | After validation | On create, increment |
| StatsEndpoints | Before query | After query | On any mutation |
| CleanupService | — | — | Bulk on cleanup |

### Invalidation Events

- Bucket create: set bucket cache, invalidate stats
- Bucket update: invalidate bucket + stats
- Bucket delete: invalidate bucket + all files/short URLs/tokens for bucket + stats
- File create/update: invalidate file + bucket + stats
- File delete: invalidate file + short URL + bucket + stats
- Short URL delete: invalidate short URL
- Upload token create: set cache, invalidate stats
- Upload token increment: invalidate token

### Files Changed

- New: `ICacheService.cs`, `CacheService.cs`
- Modified: `DependencyInjection.cs`, `BucketService.cs`, `FileService.cs`, `UploadService.cs`, `ShortUrlService.cs`, `UploadTokenService.cs`, `CleanupService.cs`, `StatsEndpoints.cs`
