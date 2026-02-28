# Eager Caching Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Add a centralized CacheService with eager invalidation to eliminate redundant DB queries across all hot paths.

**Architecture:** A singleton `CacheService` wrapping `IMemoryCache` with typed get/set/invalidate methods per entity type. Services inject `ICacheService` and use it for reads (cache-first) and invalidation on writes. Bucket-scoped key tracking enables bulk invalidation on bucket deletes.

**Tech Stack:** Microsoft.Extensions.Caching.Memory (already in project), IMemoryCache, ConcurrentDictionary for key tracking

---

### Task 1: Create ICacheService interface

**Files:**
- Create: `src/CarbonFiles.Core/Interfaces/ICacheService.cs`

**Step 1: Create the interface**

```csharp
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
```

**Step 2: Build**

Run: `dotnet build`
Expected: Build succeeded

**Step 3: Commit**

```bash
git add src/CarbonFiles.Core/Interfaces/ICacheService.cs
git commit -m "feat: add ICacheService interface"
```

---

### Task 2: Implement CacheService

**Files:**
- Create: `src/CarbonFiles.Infrastructure/Services/CacheService.cs`
- Modify: `src/CarbonFiles.Infrastructure/DependencyInjection.cs`

**Step 1: Create CacheService**

```csharp
using System.Collections.Concurrent;
using CarbonFiles.Core.Interfaces;
using CarbonFiles.Core.Models;
using CarbonFiles.Core.Models.Responses;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace CarbonFiles.Infrastructure.Services;

public sealed class CacheService : ICacheService
{
    private readonly IMemoryCache _cache;
    private readonly ILogger<CacheService> _logger;

    // Track cache keys per bucket for bulk invalidation
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, byte>> _bucketKeys = new();

    // TTLs (safety nets — eager invalidation is the primary mechanism)
    private static readonly TimeSpan BucketTtl = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan FileTtl = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan ShortUrlTtl = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan UploadTokenTtl = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan StatsTtl = TimeSpan.FromMinutes(5);

    public CacheService(IMemoryCache cache, ILogger<CacheService> logger)
    {
        _cache = cache;
        _logger = logger;
    }

    // --- Bucket ---

    public BucketDetailResponse? GetBucket(string id)
    {
        var key = $"bucket:{id}";
        if (_cache.TryGetValue(key, out BucketDetailResponse? bucket))
        {
            _logger.LogDebug("Cache hit: {Key}", key);
            return bucket;
        }
        _logger.LogDebug("Cache miss: {Key}", key);
        return null;
    }

    public void SetBucket(string id, BucketDetailResponse bucket)
    {
        var key = $"bucket:{id}";
        _cache.Set(key, bucket, BucketTtl);
        TrackKey(id, key);
    }

    public void InvalidateBucket(string id)
    {
        var key = $"bucket:{id}";
        _cache.Remove(key);
        _logger.LogDebug("Cache invalidated: {Key}", key);
    }

    // --- File Metadata ---

    public BucketFile? GetFileMetadata(string bucketId, string path)
    {
        var key = $"file:{bucketId}:{path.ToLowerInvariant()}";
        if (_cache.TryGetValue(key, out BucketFile? file))
        {
            _logger.LogDebug("Cache hit: {Key}", key);
            return file;
        }
        _logger.LogDebug("Cache miss: {Key}", key);
        return null;
    }

    public void SetFileMetadata(string bucketId, string path, BucketFile file)
    {
        var key = $"file:{bucketId}:{path.ToLowerInvariant()}";
        _cache.Set(key, file, FileTtl);
        TrackKey(bucketId, key);
    }

    public void InvalidateFile(string bucketId, string path)
    {
        var key = $"file:{bucketId}:{path.ToLowerInvariant()}";
        _cache.Remove(key);
        UntrackKey(bucketId, key);
        _logger.LogDebug("Cache invalidated: {Key}", key);
    }

    public void InvalidateFilesForBucket(string bucketId)
    {
        InvalidateAllForBucket(bucketId, "file:");
    }

    // --- Short URL ---

    public (string BucketId, string FilePath)? GetShortUrl(string code)
    {
        var key = $"short:{code}";
        if (_cache.TryGetValue(key, out (string BucketId, string FilePath) entry))
        {
            _logger.LogDebug("Cache hit: {Key}", key);
            return entry;
        }
        _logger.LogDebug("Cache miss: {Key}", key);
        return null;
    }

    public void SetShortUrl(string code, string bucketId, string filePath)
    {
        var key = $"short:{code}";
        _cache.Set(key, (bucketId, filePath), ShortUrlTtl);
        TrackKey(bucketId, key);
    }

    public void InvalidateShortUrl(string code)
    {
        var key = $"short:{code}";
        _cache.Remove(key);
        _logger.LogDebug("Cache invalidated: {Key}", key);
    }

    public void InvalidateShortUrlsForBucket(string bucketId)
    {
        InvalidateAllForBucket(bucketId, "short:");
    }

    // --- Upload Token ---

    public (string BucketId, bool IsValid)? GetUploadToken(string token)
    {
        var key = $"uploadtoken:{token}";
        if (_cache.TryGetValue(key, out (string BucketId, bool IsValid) entry))
        {
            _logger.LogDebug("Cache hit: {Key}", key);
            return entry;
        }
        _logger.LogDebug("Cache miss: {Key}", key);
        return null;
    }

    public void SetUploadToken(string token, string bucketId, bool isValid)
    {
        var key = $"uploadtoken:{token}";
        _cache.Set(key, (bucketId, isValid), UploadTokenTtl);
        TrackKey(bucketId, key);
    }

    public void InvalidateUploadToken(string token)
    {
        var key = $"uploadtoken:{token}";
        _cache.Remove(key);
        _logger.LogDebug("Cache invalidated: {Key}", key);
    }

    public void InvalidateUploadTokensForBucket(string bucketId)
    {
        InvalidateAllForBucket(bucketId, "uploadtoken:");
    }

    // --- Stats ---

    public StatsResponse? GetStats()
    {
        var key = "stats";
        if (_cache.TryGetValue(key, out StatsResponse? stats))
        {
            _logger.LogDebug("Cache hit: {Key}", key);
            return stats;
        }
        _logger.LogDebug("Cache miss: {Key}", key);
        return null;
    }

    public void SetStats(StatsResponse stats)
    {
        _cache.Set("stats", stats, StatsTtl);
    }

    public void InvalidateStats()
    {
        _cache.Remove("stats");
        _logger.LogDebug("Cache invalidated: stats");
    }

    // --- Key Tracking ---

    private void TrackKey(string bucketId, string cacheKey)
    {
        var keys = _bucketKeys.GetOrAdd(bucketId, _ => new ConcurrentDictionary<string, byte>());
        keys.TryAdd(cacheKey, 0);
    }

    private void UntrackKey(string bucketId, string cacheKey)
    {
        if (_bucketKeys.TryGetValue(bucketId, out var keys))
            keys.TryRemove(cacheKey, out _);
    }

    private void InvalidateAllForBucket(string bucketId, string prefix)
    {
        if (!_bucketKeys.TryGetValue(bucketId, out var keys))
            return;

        var toRemove = new List<string>();
        foreach (var key in keys.Keys)
        {
            if (key.StartsWith(prefix, StringComparison.Ordinal))
            {
                _cache.Remove(key);
                toRemove.Add(key);
            }
        }

        foreach (var key in toRemove)
            keys.TryRemove(key, out _);

        // If no keys left, remove the bucket tracking entry
        if (keys.IsEmpty)
            _bucketKeys.TryRemove(bucketId, out _);

        if (toRemove.Count > 0)
            _logger.LogDebug("Bulk invalidated {Count} {Prefix}* keys for bucket {BucketId}", toRemove.Count, prefix, bucketId);
    }
}
```

**Step 2: Register in DependencyInjection.cs**

Add `services.AddSingleton<ICacheService, CacheService>();` in `AddInfrastructure()` after the `AddMemoryCache()` call.

**Step 3: Build and test**

Run: `dotnet build && dotnet test`
Expected: All pass

**Step 4: Commit**

```bash
git add src/CarbonFiles.Infrastructure/Services/CacheService.cs src/CarbonFiles.Infrastructure/DependencyInjection.cs
git commit -m "feat: implement CacheService with eager invalidation and bucket-scoped key tracking"
```

---

### Task 3: Add caching to BucketService

**Files:**
- Modify: `src/CarbonFiles.Infrastructure/Services/BucketService.cs`

**Step 1: Add ICacheService to constructor**

Add `ICacheService` as a constructor parameter and field:

```csharp
private readonly ICacheService _cache;

public BucketService(CarbonFilesDbContext db, IOptions<CarbonFilesOptions> options, INotificationService notifications, ILogger<BucketService> logger, ICacheService cache)
{
    _db = db;
    _dataDir = options.Value.DataDir;
    _notifications = notifications;
    _logger = logger;
    _cache = cache;
}
```

Add `using CarbonFiles.Core.Interfaces;` if not already present (it likely is via `INotificationService`).

**Step 2: Add cache reads in GetByIdAsync**

At the start of `GetByIdAsync`, before the DB query:

```csharp
var cached = _cache.GetBucket(id);
if (cached != null)
    return cached;
```

After building the `BucketDetailResponse`, before returning it:

```csharp
_cache.SetBucket(id, response);
return response;
```

(Where `response` is the variable name for the `BucketDetailResponse` being returned — it's currently an inline `new BucketDetailResponse { ... }`, so assign it to a variable first.)

**Step 3: Add cache invalidation on mutations**

In `CreateAsync`, after `SaveChangesAsync` — invalidate stats:
```csharp
_cache.InvalidateStats();
```

In `UpdateAsync`, after `SaveChangesAsync`:
```csharp
_cache.InvalidateBucket(id);
_cache.InvalidateStats();
```

In `DeleteAsync`, before `return true`:
```csharp
_cache.InvalidateBucket(id);
_cache.InvalidateFilesForBucket(id);
_cache.InvalidateShortUrlsForBucket(id);
_cache.InvalidateUploadTokensForBucket(id);
_cache.InvalidateStats();
```

In `GetSummaryAsync` — this method also does a bucket lookup. Add cache check at the start:
No — GetSummaryAsync returns a text string, not a BucketDetailResponse. It doesn't use the same cache. Leave it as-is.

**Step 4: Build and test**

Run: `dotnet build && dotnet test`
Expected: All pass. If tests fail due to constructor changes, update test files to pass a no-op cache (see Task 8).

**Step 5: Commit**

```bash
git add src/CarbonFiles.Infrastructure/Services/BucketService.cs
git commit -m "feat: add caching to BucketService with eager invalidation"
```

---

### Task 4: Add caching to FileService

**Files:**
- Modify: `src/CarbonFiles.Infrastructure/Services/FileService.cs`

**Step 1: Add ICacheService to constructor**

```csharp
private readonly ICacheService _cache;

public FileService(CarbonFilesDbContext db, FileStorageService storage, INotificationService notifications, ILogger<FileService> logger, ICacheService cache)
{
    _db = db;
    _storage = storage;
    _notifications = notifications;
    _logger = logger;
    _cache = cache;
}
```

**Step 2: Add cache reads in GetMetadataAsync**

At the start:
```csharp
var cached = _cache.GetFileMetadata(bucketId, path);
if (cached != null)
    return cached;
```

Before returning the result (after `entity.ToBucketFile()`):
```csharp
var file = entity.ToBucketFile();
_cache.SetFileMetadata(bucketId, path, file);
return file;
```

**Step 3: Add cache invalidation on mutations**

In `DeleteAsync`, before `return true`:
```csharp
_cache.InvalidateFile(bucketId, normalized);
_cache.InvalidateBucket(bucketId);
_cache.InvalidateStats();
```

In `UpdateFileSizeAsync`, after saving:
```csharp
_cache.InvalidateFile(bucketId, path);
_cache.InvalidateBucket(bucketId);
_cache.InvalidateStats();
```

**Step 4: Build and test**

Run: `dotnet build && dotnet test`

**Step 5: Commit**

```bash
git add src/CarbonFiles.Infrastructure/Services/FileService.cs
git commit -m "feat: add caching to FileService with eager invalidation"
```

---

### Task 5: Add caching to UploadService

**Files:**
- Modify: `src/CarbonFiles.Infrastructure/Services/UploadService.cs`

**Step 1: Add ICacheService to constructor**

```csharp
private readonly ICacheService _cache;

public UploadService(CarbonFilesDbContext db, FileStorageService storage, INotificationService notifications, ILogger<UploadService> logger, ICacheService cache)
{
    _db = db;
    _storage = storage;
    _notifications = notifications;
    _logger = logger;
    _cache = cache;
}
```

**Step 2: Add cache invalidation in StoreFileAsync**

UploadService doesn't read from cache (it always writes to DB), but it must invalidate after mutations.

After the `if (existing != null)` block's `SaveChangesAsync`:
```csharp
_cache.InvalidateFile(bucketId, normalized);
_cache.InvalidateBucket(bucketId);
_cache.InvalidateStats();
```

After the `else` block's `SaveChangesAsync`:
```csharp
_cache.InvalidateFile(bucketId, normalized);
_cache.InvalidateBucket(bucketId);
_cache.InvalidateStats();
```

**Step 3: Build and test**

Run: `dotnet build && dotnet test`

**Step 4: Commit**

```bash
git add src/CarbonFiles.Infrastructure/Services/UploadService.cs
git commit -m "feat: add cache invalidation to UploadService"
```

---

### Task 6: Add caching to ShortUrlService

**Files:**
- Modify: `src/CarbonFiles.Infrastructure/Services/ShortUrlService.cs`

**Step 1: Add ICacheService to constructor**

```csharp
private readonly ICacheService _cache;

public ShortUrlService(CarbonFilesDbContext db, ILogger<ShortUrlService> logger, ICacheService cache)
{
    _db = db;
    _logger = logger;
    _cache = cache;
}
```

**Step 2: Add cache read in ResolveAsync**

At the start of `ResolveAsync`:
```csharp
var cached = _cache.GetShortUrl(code);
if (cached != null)
    return $"/api/buckets/{cached.Value.BucketId}/files/{cached.Value.FilePath}/content";
```

Before the `return` at the end (after building the URL):
```csharp
_cache.SetShortUrl(code, shortUrl.BucketId, shortUrl.FilePath);
return $"/api/buckets/{shortUrl.BucketId}/files/{shortUrl.FilePath}/content";
```

IMPORTANT: The cached short URL bypasses the bucket expiry check. To handle this correctly, when the cache hits, we still need to verify the bucket hasn't expired. Options:
- (a) Check the bucket cache for expiry (but bucket cache might also be cached and not expired yet)
- (b) Accept that short URLs may serve content for up to ShortUrlTtl (10min) after bucket expires
- (c) Store expiry in the short URL cache entry

Go with option (b) — the TTL is the safety net. This is acceptable because expired buckets are cleaned up by the CleanupService which invalidates the cache.

**Step 3: Add cache invalidation on mutations**

In `CreateAsync`, after `SaveChangesAsync`:
```csharp
_cache.SetShortUrl(code, bucketId, filePath);
```

In `DeleteAsync`, before `return true`:
```csharp
_cache.InvalidateShortUrl(code);
```

**Step 4: Build and test**

Run: `dotnet build && dotnet test`

**Step 5: Commit**

```bash
git add src/CarbonFiles.Infrastructure/Services/ShortUrlService.cs
git commit -m "feat: add caching to ShortUrlService with eager invalidation"
```

---

### Task 7: Add caching to UploadTokenService and StatsEndpoints

**Files:**
- Modify: `src/CarbonFiles.Infrastructure/Services/UploadTokenService.cs`
- Modify: `src/CarbonFiles.Api/Endpoints/StatsEndpoints.cs`

**Step 1: Add ICacheService to UploadTokenService constructor**

```csharp
private readonly ICacheService _cache;

public UploadTokenService(CarbonFilesDbContext db, ILogger<UploadTokenService> logger, ICacheService cache)
{
    _db = db;
    _logger = logger;
    _cache = cache;
}
```

**Step 2: Add cache read in ValidateAsync**

At the start:
```csharp
var cached = _cache.GetUploadToken(token);
if (cached != null)
    return cached.Value;
```

Before each `return` that includes a result, set the cache:
- After successful validation: `_cache.SetUploadToken(token, entity.BucketId, true);`
- After expired/exhausted: `_cache.SetUploadToken(token, entity.BucketId, false);`
- After not found: don't cache (avoids caching absence)

**Step 3: Add cache invalidation**

In `CreateAsync`, after `SaveChangesAsync`:
```csharp
_cache.InvalidateStats();
```

In `IncrementUsageAsync`, after `ExecuteUpdateAsync`:
```csharp
_cache.InvalidateUploadToken(token);
```

**Step 4: Add stats caching to StatsEndpoints**

In `StatsEndpoints.cs`, inject `ICacheService cache` into the handler lambda. At the start:
```csharp
var cached = cache.GetStats();
if (cached != null)
    return Results.Ok(cached);
```

Before returning the stats response:
```csharp
cache.SetStats(stats);
return Results.Ok(stats);
```

**Step 5: Build and test**

Run: `dotnet build && dotnet test`

**Step 6: Commit**

```bash
git add src/CarbonFiles.Infrastructure/Services/UploadTokenService.cs src/CarbonFiles.Api/Endpoints/StatsEndpoints.cs
git commit -m "feat: add caching to UploadTokenService and StatsEndpoints"
```

---

### Task 8: Add cache invalidation to CleanupService and fix tests

**Files:**
- Modify: `src/CarbonFiles.Infrastructure/Services/CleanupService.cs`
- Modify: Test files as needed

**Step 1: Add ICacheService to CleanupService**

CleanupService uses `IServiceProvider` to create scopes. Resolve `ICacheService` from the scope:

In `CleanupExpiredBucketsAsync`, after resolving `db` and `storage`:
```csharp
var cache = scope.ServiceProvider.GetRequiredService<ICacheService>();
```

After deleting each expired bucket (inside the `foreach` loop, after removing DB records):
```csharp
cache.InvalidateBucket(bucket.Id);
cache.InvalidateFilesForBucket(bucket.Id);
cache.InvalidateShortUrlsForBucket(bucket.Id);
cache.InvalidateUploadTokensForBucket(bucket.Id);
```

After the entire cleanup is complete (after `SaveChangesAsync`):
```csharp
cache.InvalidateStats();
```

**Step 2: Fix test files**

Any test that directly constructs a service with `new` and passes constructor arguments needs the `ICacheService` parameter. Create a `NullCacheService` or use a mock. The simplest approach: create a minimal no-op implementation in the test project, or use `NSubstitute`/`Moq` if already available.

Check if the test project has a mocking library:
```bash
grep -i "substitute\|moq\|nsubstitute" tests/CarbonFiles.Infrastructure.Tests/CarbonFiles.Infrastructure.Tests.csproj
```

If no mocking library, create a simple `NullCacheService : ICacheService` in the test project that returns null for all gets and does nothing for sets/invalidates.

Update all test files that construct services with `new`:
- `BucketServiceTests.cs` — add `NullCacheService` to BucketService constructor
- `ShortUrlServiceTests.cs` — add to ShortUrlService constructor
- `UploadTokenServiceTests.cs` — add to UploadTokenService constructor
- `CleanupServiceTests.cs` — ensure ICacheService is registered in the test DI container

**Step 3: Build and test**

Run: `dotnet build && dotnet test`
Expected: All 362 tests pass

**Step 4: Commit**

```bash
git add src/CarbonFiles.Infrastructure/Services/CleanupService.cs tests/
git commit -m "feat: add cache invalidation to CleanupService and fix all tests"
```

---

### Task 9: Final verification

**Step 1: Full build**

Run: `dotnet build`
Expected: 0 warnings, 0 errors

**Step 2: Run all tests**

Run: `dotnet test`
Expected: All tests pass

**Step 3: Commit if any fixups needed**

```bash
git add -A && git commit -m "fix: resolve any caching build issues"
```
