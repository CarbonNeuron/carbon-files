# Content-Addressable Storage + Directory Browsing Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Add content-addressable storage (SHA256-based dedup) and unify file listing into a single `/files` endpoint with optional tree browsing.

**Architecture:** Files stored by SHA256 hash under `data/content/{ab}/{cd}/{hash}`. `ContentObjects` table tracks unique content with ref-counting. `Files` table gains `ContentHash` FK. Upload streams hash inline via `IncrementalHash`. Existing `/files` gains `delimiter`/`prefix` params for tree mode; `/ls` is removed.

**Tech Stack:** .NET 10, SQLite/Dapper, System.IO.Pipelines, IncrementalHash (SHA256), xUnit + FluentAssertions

**Design doc:** `docs/plans/2026-03-04-cas-directory-browsing-design.md`

---

### Task 1: Core Models and Schema

Add `ContentObject` entity, update `BucketFile` model, update schema DDL, update JSON serialization context.

**Files:**
- Create: `src/CarbonFiles.Infrastructure/Data/Entities/ContentObjectEntity.cs`
- Modify: `src/CarbonFiles.Infrastructure/Data/Entities/FileEntity.cs`
- Modify: `src/CarbonFiles.Core/Models/BucketFile.cs`
- Modify: `src/CarbonFiles.Core/Models/Responses/UploadResponse.cs`
- Modify: `src/CarbonFiles.Core/Models/Responses/BucketDetailResponse.cs`
- Modify: `src/CarbonFiles.Core/Models/Responses/DirectoryListingResponse.cs`
- Modify: `src/CarbonFiles.Infrastructure/Data/DatabaseInitializer.cs`
- Modify: `src/CarbonFiles.Api/Serialization/CarbonFilesJsonContext.cs`

**Step 1: Create ContentObjectEntity**

```csharp
// src/CarbonFiles.Infrastructure/Data/Entities/ContentObjectEntity.cs
using Microsoft.Data.Sqlite;

namespace CarbonFiles.Infrastructure.Data.Entities;

public sealed class ContentObjectEntity
{
    public required string Hash { get; set; }
    public long Size { get; set; }
    public required string DiskPath { get; set; }
    public int RefCount { get; set; }
    public DateTime CreatedAt { get; set; }

    internal static ContentObjectEntity Read(SqliteDataReader r) => new()
    {
        Hash = r.GetString(r.GetOrdinal("Hash")),
        Size = r.GetInt64(r.GetOrdinal("Size")),
        DiskPath = r.GetString(r.GetOrdinal("DiskPath")),
        RefCount = r.GetInt32(r.GetOrdinal("RefCount")),
        CreatedAt = r.GetDateTime(r.GetOrdinal("CreatedAt")),
    };
}
```

**Step 2: Add ContentHash to FileEntity**

In `src/CarbonFiles.Infrastructure/Data/Entities/FileEntity.cs`, add property and update `Read`:

```csharp
public string? ContentHash { get; set; }
```

In the `Read` method, add after `UpdatedAt`:

```csharp
ContentHash = r.IsDBNull(r.GetOrdinal("ContentHash")) ? null : r.GetString(r.GetOrdinal("ContentHash")),
```

Also add/update the `ToBucketFile()` extension. If `FileEntity` doesn't have one defined in the entity file, find where it's defined (likely as an extension or inline in services). The mapping must now include `Sha256 = ContentHash`.

**Step 3: Add Sha256 to BucketFile model**

In `src/CarbonFiles.Core/Models/BucketFile.cs`, add:

```csharp
public string? Sha256 { get; set; }
```

**Step 4: Create UploadedFile model for upload responses**

The upload response needs a `Deduplicated` field that regular `BucketFile` doesn't have. Create a new model:

```csharp
// src/CarbonFiles.Core/Models/Responses/UploadedFile.cs
namespace CarbonFiles.Core.Models.Responses;

public sealed class UploadedFile
{
    public required string Path { get; init; }
    public required string Name { get; init; }
    public long Size { get; set; }
    public required string MimeType { get; init; }
    public string? ShortCode { get; set; }
    public string? ShortUrl { get; set; }
    public string? Sha256 { get; set; }
    public bool Deduplicated { get; set; }
    public DateTime CreatedAt { get; init; }
    public DateTime UpdatedAt { get; set; }
}
```

Update `UploadResponse` to use it:

```csharp
public sealed class UploadResponse
{
    public required IReadOnlyList<UploadedFile> Uploaded { get; init; }
}
```

**Step 5: Update BucketDetailResponse**

In `src/CarbonFiles.Core/Models/Responses/BucketDetailResponse.cs`:

- Make `Files` nullable (null when `?include=files` not set)
- Add dedup stats fields:

```csharp
public sealed class BucketDetailResponse
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Owner { get; init; }
    public string? Description { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime? ExpiresAt { get; init; }
    public DateTime? LastUsedAt { get; init; }
    public int FileCount { get; init; }
    public long TotalSize { get; init; }
    public int UniqueContentCount { get; init; }
    public long UniqueContentSize { get; init; }
    public IReadOnlyList<BucketFile>? Files { get; init; }
    public bool? HasMoreFiles { get; init; }
}
```

**Step 6: Replace DirectoryListingResponse with tree-mode fields**

The unified `/files` endpoint in tree mode needs a different response shape. Create a new response:

```csharp
// src/CarbonFiles.Core/Models/Responses/FileTreeResponse.cs
namespace CarbonFiles.Core.Models.Responses;

public sealed class FileTreeResponse
{
    public string? Prefix { get; init; }
    public required string Delimiter { get; init; }
    public required IReadOnlyList<DirectoryEntry> Directories { get; init; }
    public required IReadOnlyList<BucketFile> Files { get; init; }
    public int TotalFiles { get; init; }
    public int TotalDirectories { get; init; }
    public string? Cursor { get; init; }
}

public sealed class DirectoryEntry
{
    public required string Path { get; init; }
    public int FileCount { get; init; }
    public long TotalSize { get; init; }
}
```

Keep `DirectoryListingResponse` for now (remove in Task 9 when `/ls` is replaced).

**Step 7: Update DatabaseInitializer.Schema**

Add `ContentObjects` table and `ContentHash` column to the schema constant. Since SQLite doesn't support `ALTER TABLE ADD COLUMN` in `IF NOT EXISTS` style, we need the full schema to include the column for new databases, and handle migration separately. Update the `Files` CREATE TABLE:

```sql
CREATE TABLE IF NOT EXISTS "ContentObjects" (
    "Hash" TEXT NOT NULL CONSTRAINT "PK_ContentObjects" PRIMARY KEY,
    "Size" INTEGER NOT NULL,
    "DiskPath" TEXT NOT NULL,
    "RefCount" INTEGER NOT NULL DEFAULT 1,
    "CreatedAt" TEXT NOT NULL
);

CREATE INDEX IF NOT EXISTS "IX_ContentObjects_Orphans"
    ON "ContentObjects" ("RefCount") WHERE "RefCount" = 0;
```

Add `"ContentHash" TEXT NULL` to the `Files` CREATE TABLE statement, and add:

```sql
CREATE INDEX IF NOT EXISTS "IX_Files_ContentHash" ON "Files" ("ContentHash");
```

**Step 8: Update JSON serialization context**

In `src/CarbonFiles.Api/Serialization/CarbonFilesJsonContext.cs`, add:

```csharp
[JsonSerializable(typeof(UploadedFile))]
[JsonSerializable(typeof(FileTreeResponse))]
[JsonSerializable(typeof(DirectoryEntry))]
[JsonSerializable(typeof(VerifyResponse))]  // for Task 11
```

Also create `VerifyResponse`:

```csharp
// src/CarbonFiles.Core/Models/Responses/VerifyResponse.cs
namespace CarbonFiles.Core.Models.Responses;

public sealed class VerifyResponse
{
    public required string Path { get; init; }
    public required string StoredHash { get; init; }
    public required string ComputedHash { get; init; }
    public bool Valid { get; init; }
}
```

**Step 9: Build and verify compilation**

Run: `dotnet build`
Expected: Build succeeds (some warnings about unused types are fine at this stage).

**Step 10: Commit**

```
feat: add CAS models, schema, and response types

ContentObjectEntity, ContentHash on FileEntity, Sha256 on BucketFile,
UploadedFile with Deduplicated flag, FileTreeResponse, VerifyResponse,
updated DatabaseInitializer schema.
```

---

### Task 2: PathNormalizer Utility

Add path normalization applied on uploads. TDD.

**Files:**
- Create: `src/CarbonFiles.Core/Utilities/PathNormalizer.cs`
- Create: `tests/CarbonFiles.Api.Tests/Utilities/PathNormalizerTests.cs`

**Step 1: Write failing tests**

```csharp
// tests/CarbonFiles.Api.Tests/Utilities/PathNormalizerTests.cs
using CarbonFiles.Core.Utilities;
using FluentAssertions;

namespace CarbonFiles.Api.Tests.Utilities;

public class PathNormalizerTests
{
    [Theory]
    [InlineData("readme.md", "readme.md")]
    [InlineData("/readme.md", "readme.md")]
    [InlineData("src\\main.cs", "src/main.cs")]
    [InlineData("src//utils//file.cs", "src/utils/file.cs")]
    [InlineData("src/main.cs/", "src/main.cs")]
    [InlineData("/src/main.cs", "src/main.cs")]
    public void Normalize_ValidPaths_ReturnsNormalized(string input, string expected)
    {
        PathNormalizer.Normalize(input).Should().Be(expected);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Normalize_EmptyOrNull_ThrowsArgumentException(string? input)
    {
        var act = () => PathNormalizer.Normalize(input!);
        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData("../etc/passwd")]
    [InlineData("src/../secret")]
    [InlineData("..")]
    public void Normalize_PathTraversal_ThrowsArgumentException(string input)
    {
        var act = () => PathNormalizer.Normalize(input);
        act.Should().Throw<ArgumentException>().WithMessage("*traversal*");
    }

    [Theory]
    [InlineData("src//")]
    [InlineData("src/./file")]
    public void Normalize_EmptyComponents_ThrowsArgumentException(string input)
    {
        var act = () => PathNormalizer.Normalize(input);
        act.Should().Throw<ArgumentException>();
    }
}
```

**Step 2: Run tests to verify they fail**

Run: `dotnet test tests/CarbonFiles.Api.Tests --filter "PathNormalizerTests" -v m`
Expected: FAIL — `PathNormalizer` type not found.

**Step 3: Implement PathNormalizer**

```csharp
// src/CarbonFiles.Core/Utilities/PathNormalizer.cs
namespace CarbonFiles.Core.Utilities;

public static class PathNormalizer
{
    public static string Normalize(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("Path cannot be empty");

        // Backslash to forward slash
        path = path.Replace('\\', '/');

        // Remove leading/trailing slashes
        path = path.Trim('/');

        // Collapse double slashes
        while (path.Contains("//"))
            path = path.Replace("//", "/");

        if (string.IsNullOrEmpty(path))
            throw new ArgumentException("Path cannot be empty");

        // Reject path traversal
        if (path.Contains(".."))
            throw new ArgumentException("Path traversal not allowed");

        // Reject empty path components (e.g., "src/./file" after normalization still has ".")
        var components = path.Split('/');
        foreach (var component in components)
        {
            if (string.IsNullOrWhiteSpace(component) || component == ".")
                throw new ArgumentException("Empty path component");
        }

        return path;
    }
}
```

**Step 4: Run tests to verify they pass**

Run: `dotnet test tests/CarbonFiles.Api.Tests --filter "PathNormalizerTests" -v m`
Expected: All PASS.

**Step 5: Commit**

```
feat: add PathNormalizer utility with tests

Normalizes upload paths: backslash conversion, slash trimming,
traversal rejection, empty component rejection.
```

---

### Task 3: Content Storage Service

New service for CAS disk operations: store content by hash, resolve paths, delete content files.

**Files:**
- Create: `src/CarbonFiles.Infrastructure/Services/ContentStorageService.cs`
- Modify: `src/CarbonFiles.Infrastructure/DependencyInjection.cs`

**Step 1: Write ContentStorageService**

```csharp
// src/CarbonFiles.Infrastructure/Services/ContentStorageService.cs
using CarbonFiles.Core.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CarbonFiles.Infrastructure.Services;

public sealed class ContentStorageService
{
    private readonly string _contentDir;
    private readonly ILogger<ContentStorageService> _logger;

    public ContentStorageService(IOptions<CarbonFilesOptions> options, ILogger<ContentStorageService> logger)
    {
        _contentDir = Path.Combine(options.Value.DataDir, "content");
        _logger = logger;
    }

    /// <summary>
    /// Computes the relative disk path for a given SHA256 hash.
    /// Format: ab/cd/abcdef1234...
    /// </summary>
    public static string ComputeDiskPath(string hash)
    {
        return Path.Combine(hash[..2], hash[2..4], hash);
    }

    /// <summary>
    /// Returns the full absolute path for a content object.
    /// </summary>
    public string GetFullPath(string diskPath)
    {
        return Path.Combine(_contentDir, diskPath);
    }

    /// <summary>
    /// Moves a temp file into the content-addressed store.
    /// Creates the sharded directory structure if needed.
    /// </summary>
    public void MoveToContentStore(string tempPath, string diskPath)
    {
        var fullPath = GetFullPath(diskPath);
        var dir = Path.GetDirectoryName(fullPath)!;
        Directory.CreateDirectory(dir);
        File.Move(tempPath, fullPath, overwrite: false);
        _logger.LogDebug("Stored content at {Path}", fullPath);
    }

    /// <summary>
    /// Checks if a content file exists on disk.
    /// </summary>
    public bool Exists(string diskPath)
    {
        return File.Exists(GetFullPath(diskPath));
    }

    /// <summary>
    /// Deletes a content file from disk.
    /// </summary>
    public void Delete(string diskPath)
    {
        var fullPath = GetFullPath(diskPath);
        if (File.Exists(fullPath))
        {
            File.Delete(fullPath);
            _logger.LogDebug("Deleted content at {Path}", fullPath);
        }
    }

    /// <summary>
    /// Opens a content file for reading.
    /// </summary>
    public FileStream? OpenRead(string diskPath)
    {
        var fullPath = GetFullPath(diskPath);
        return File.Exists(fullPath)
            ? new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920)
            : null;
    }
}
```

**Step 2: Register in DI**

In `src/CarbonFiles.Infrastructure/DependencyInjection.cs`, add alongside `FileStorageService`:

```csharp
services.AddSingleton<ContentStorageService>();
```

**Step 3: Build**

Run: `dotnet build`
Expected: Success.

**Step 4: Commit**

```
feat: add ContentStorageService for CAS disk operations

Handles sharded content storage layout (ab/cd/hash), move-to-store,
read, delete, and path resolution.
```

---

### Task 4: Hashing in FileStorageService

Modify `FileStorageService.StoreAsync` to compute SHA256 while streaming. Return both size and hash.

**Files:**
- Modify: `src/CarbonFiles.Infrastructure/Services/FileStorageService.cs`
- Modify: `src/CarbonFiles.Infrastructure/Services/UploadService.cs` (update call site — signature change)

**Step 1: Add hash result type and modify StoreAsync**

In `FileStorageService.cs`:

1. Add a result record at the top of the file (or as a nested type):

```csharp
public sealed record StoreResult(long Size, string Sha256Hash);
```

2. Change `StoreAsync` return type from `Task<long>` to `Task<StoreResult>`.

3. In `FillPipeFromStreamAsync`, add `IncrementalHash` parameter. Hash each chunk as it flows through:

```csharp
private static async Task<long> FillPipeFromStreamAsync(
    PipeWriter writer, Stream source, long maxSize,
    System.Security.Cryptography.IncrementalHash hash,
    CancellationToken ct)
{
    const int MinimumReadSize = 128 * 1024;
    long totalBytes = 0;

    try
    {
        while (true)
        {
            var memory = writer.GetMemory(MinimumReadSize);
            var bytesRead = await source.ReadAsync(memory, ct);
            if (bytesRead == 0)
                break;

            totalBytes += bytesRead;
            if (maxSize > 0 && totalBytes > maxSize)
                throw new FileTooLargeException(maxSize);

            hash.AppendData(memory.Span[..bytesRead]);
            writer.Advance(bytesRead);

            var flush = await writer.FlushAsync(ct);
            if (flush.IsCompleted)
                break;
        }
    }
    catch (Exception ex)
    {
        await writer.CompleteAsync(ex);
        throw;
    }

    await writer.CompleteAsync();
    return totalBytes;
}
```

4. In `StoreAsync`, create the `IncrementalHash` and extract the final hash:

```csharp
public async Task<StoreResult> StoreAsync(string bucketId, string filePath, Stream content, long maxSize = 0, CancellationToken ct = default)
{
    var targetPath = GetFilePath(bucketId, filePath);
    var dir = Path.GetDirectoryName(targetPath)!;
    Directory.CreateDirectory(dir);

    var tempPath = $"{targetPath}.tmp.{Guid.NewGuid():N}";

    using var sha256 = System.Security.Cryptography.IncrementalHash.CreateHash(
        System.Security.Cryptography.HashAlgorithmName.SHA256);

    var pipe = new Pipe(new PipeOptions(
        pauseWriterThreshold: 1024 * 1024,
        resumeWriterThreshold: 512 * 1024,
        minimumSegmentSize: 128 * 1024,
        useSynchronizationContext: false));

    var fillTask = FillPipeFromStreamAsync(pipe.Writer, content, maxSize, sha256, ct);
    var drainTask = DrainPipeToFileAsync(pipe.Reader, tempPath, ct);

    long totalBytes;
    try
    {
        await Task.WhenAll(fillTask, drainTask);
        totalBytes = fillTask.Result;
    }
    catch
    {
        try { File.Delete(tempPath); } catch { /* best-effort cleanup */ }
        if (fillTask.IsFaulted)
            throw fillTask.Exception!.InnerException!;
        throw;
    }

    var hashHex = Convert.ToHexStringLower(sha256.GetHashAndReset());

    // Don't move to final bucket path — caller (UploadService) will handle CAS placement
    // Just return the temp path, size, and hash
    // NOTE: We need a new method for CAS. Keep old behavior for now, refactor in Task 5.
    File.Move(tempPath, targetPath, overwrite: true);
    _logger.LogDebug("Stored {Size} bytes to {Path} (sha256={Hash})", totalBytes, targetPath, hashHex);
    return new StoreResult(totalBytes, hashHex);
}
```

**Step 2: Add a new method StoreToTempAsync for CAS uploads**

This writes to a temp file and returns the path + hash without moving to a final location. The caller handles CAS placement.

```csharp
/// <summary>
/// Streams content to a temp file, computing SHA256 inline.
/// Returns the temp file path, size, and hash. Caller is responsible for
/// moving or deleting the temp file.
/// </summary>
public async Task<(string TempPath, long Size, string Sha256Hash)> StoreToTempAsync(
    Stream content, long maxSize = 0, CancellationToken ct = default)
{
    var tempDir = Path.Combine(_dataDir, "tmp");
    Directory.CreateDirectory(tempDir);
    var tempPath = Path.Combine(tempDir, $"{Guid.NewGuid():N}.tmp");

    using var sha256 = System.Security.Cryptography.IncrementalHash.CreateHash(
        System.Security.Cryptography.HashAlgorithmName.SHA256);

    var pipe = new Pipe(new PipeOptions(
        pauseWriterThreshold: 1024 * 1024,
        resumeWriterThreshold: 512 * 1024,
        minimumSegmentSize: 128 * 1024,
        useSynchronizationContext: false));

    var fillTask = FillPipeFromStreamAsync(pipe.Writer, content, maxSize, sha256, ct);
    var drainTask = DrainPipeToFileAsync(pipe.Reader, tempPath, ct);

    long totalBytes;
    try
    {
        await Task.WhenAll(fillTask, drainTask);
        totalBytes = fillTask.Result;
    }
    catch
    {
        try { File.Delete(tempPath); } catch { /* best-effort */ }
        if (fillTask.IsFaulted)
            throw fillTask.Exception!.InnerException!;
        throw;
    }

    var hashHex = Convert.ToHexStringLower(sha256.GetHashAndReset());
    _logger.LogDebug("Stored {Size} bytes to temp {Path} (sha256={Hash})", totalBytes, tempPath, hashHex);
    return (tempPath, totalBytes, hashHex);
}
```

**Step 3: Update UploadService call site**

In `UploadService.StoreFileAsync`, change `var size = await _storage.StoreAsync(...)` to use the new `StoreResult`:

```csharp
var result = await _storage.StoreAsync(bucketId, path, content, maxSize, ct);
var size = result.Size;
```

This keeps existing behavior working while we build out the CAS flow.

**Step 4: Build and run existing tests**

Run: `dotnet test tests/CarbonFiles.Api.Tests -v m`
Expected: All existing tests pass (upload/download/delete flow unchanged functionally).

**Step 5: Commit**

```
feat: add SHA256 hashing during file upload streaming

IncrementalHash integrated into pipe fill. StoreAsync returns
StoreResult(Size, Sha256Hash). New StoreToTempAsync for CAS uploads.
```

---

### Task 5: CAS Upload Flow

Rewrite `UploadService.StoreFileAsync` to use content-addressable storage. This is the core change.

**Files:**
- Modify: `src/CarbonFiles.Infrastructure/Services/UploadService.cs`
- Modify: `src/CarbonFiles.Core/Interfaces/IUploadService.cs`
- Modify: `src/CarbonFiles.Core/Models/Responses/UploadResponse.cs`
- Test: `tests/CarbonFiles.Api.Tests/Endpoints/UploadEndpointTests.cs` (new)

**Step 1: Write failing tests for CAS upload behavior**

Create a new test file for upload-specific tests that verify CAS behavior:

```csharp
// tests/CarbonFiles.Api.Tests/Endpoints/UploadEndpointTests.cs
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using FluentAssertions;

namespace CarbonFiles.Api.Tests.Endpoints;

public class UploadEndpointTests : IntegrationTestBase
{
    private async Task<string> CreateBucketAsync(HttpClient? client = null)
    {
        client ??= Fixture.CreateAdminClient();
        var response = await client.PostAsJsonAsync("/api/buckets",
            new { name = "test-bucket" }, TestContext.Current.CancellationToken);
        response.EnsureSuccessStatusCode();
        var json = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync(TestContext.Current.CancellationToken),
            cancellationToken: TestContext.Current.CancellationToken);
        return json.RootElement.GetProperty("id").GetString()!;
    }

    private async Task<JsonElement> UploadFileAsync(HttpClient client, string bucketId, string fileName, string content)
    {
        using var form = new MultipartFormDataContent();
        form.Add(new StringContent(content), "file", fileName);
        var response = await client.PostAsync($"/api/buckets/{bucketId}/upload", form,
            TestContext.Current.CancellationToken);
        response.EnsureSuccessStatusCode();
        var json = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync(TestContext.Current.CancellationToken),
            cancellationToken: TestContext.Current.CancellationToken);
        return json.RootElement.GetProperty("uploaded")[0];
    }

    [Fact]
    public async Task Upload_ReturnsSha256Hash()
    {
        var client = Fixture.CreateAdminClient();
        var bucketId = await CreateBucketAsync(client);
        var file = await UploadFileAsync(client, bucketId, "test.txt", "hello world");

        file.GetProperty("sha256").GetString().Should().NotBeNullOrEmpty();
        file.GetProperty("sha256").GetString()!.Length.Should().Be(64); // SHA256 hex = 64 chars
    }

    [Fact]
    public async Task Upload_IdenticalContent_Deduplicates()
    {
        var client = Fixture.CreateAdminClient();
        var bucketId = await CreateBucketAsync(client);

        var file1 = await UploadFileAsync(client, bucketId, "file1.txt", "identical content");
        var file2 = await UploadFileAsync(client, bucketId, "file2.txt", "identical content");

        file1.GetProperty("sha256").GetString().Should().Be(file2.GetProperty("sha256").GetString());
        file2.GetProperty("deduplicated").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task Upload_DifferentContent_NoDeduplicate()
    {
        var client = Fixture.CreateAdminClient();
        var bucketId = await CreateBucketAsync(client);

        var file1 = await UploadFileAsync(client, bucketId, "a.txt", "content A");
        var file2 = await UploadFileAsync(client, bucketId, "b.txt", "content B");

        file1.GetProperty("sha256").GetString().Should().NotBe(file2.GetProperty("sha256").GetString());
        file2.GetProperty("deduplicated").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task Upload_SamePathOverwrite_UpdatesHash()
    {
        var client = Fixture.CreateAdminClient();
        var bucketId = await CreateBucketAsync(client);

        await UploadFileAsync(client, bucketId, "file.txt", "version 1");
        var file2 = await UploadFileAsync(client, bucketId, "file.txt", "version 2");

        file2.GetProperty("sha256").GetString().Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task Upload_CrossBucketDedup()
    {
        var client = Fixture.CreateAdminClient();
        var bucket1 = await CreateBucketAsync(client);
        var bucket2 = await CreateBucketAsync(client);

        var file1 = await UploadFileAsync(client, bucket1, "shared.txt", "shared content");
        var file2 = await UploadFileAsync(client, bucket2, "copy.txt", "shared content");

        file1.GetProperty("sha256").GetString().Should().Be(file2.GetProperty("sha256").GetString());
        file2.GetProperty("deduplicated").GetBoolean().Should().BeTrue();
    }
}
```

**Step 2: Run tests to verify they fail**

Run: `dotnet test tests/CarbonFiles.Api.Tests --filter "UploadEndpointTests" -v m`
Expected: FAIL — `sha256` and `deduplicated` properties not in response.

**Step 3: Rewrite UploadService.StoreFileAsync for CAS**

Change `IUploadService.StoreFileAsync` return type to `Task<UploadedFile>`. Update the implementation:

```csharp
// In UploadService.cs — full rewrite of StoreFileAsync
public async Task<UploadedFile> StoreFileAsync(string bucketId, string path, Stream content, AuthContext auth, long maxSize = 0, CancellationToken ct = default)
{
    _logger.LogDebug("Storing file {Path} in bucket {BucketId}", path, bucketId);

    var name = Path.GetFileName(path);
    var mimeType = MimeDetector.DetectFromExtension(path);

    // Stream to temp file, computing SHA256 inline
    var (tempPath, size, hash) = await _storage.StoreToTempAsync(content, maxSize, ct);

    bool deduplicated = false;

    try
    {
        // Check if content already exists
        var existingContent = await Db.QueryFirstOrDefaultAsync(_db,
            "SELECT * FROM ContentObjects WHERE Hash = @hash",
            p => p.AddWithValue("@hash", hash),
            ContentObjectEntity.Read);

        if (existingContent != null)
        {
            // Dedup: increment ref count, delete temp file
            deduplicated = true;
            File.Delete(tempPath);
        }
        else
        {
            // New content: move to CAS store
            var diskPath = ContentStorageService.ComputeDiskPath(hash);
            _contentStorage.MoveToContentStore(tempPath, diskPath);
        }

        // Check if file record already exists
        var existing = await Db.QueryFirstOrDefaultAsync(_db,
            "SELECT * FROM Files WHERE BucketId = @bucketId AND Path = @path",
            p =>
            {
                p.AddWithValue("@bucketId", bucketId);
                p.AddWithValue("@path", path);
            },
            FileEntity.Read);

        var now = DateTime.UtcNow;

        if (existing != null)
        {
            var oldSize = existing.Size;
            var oldHash = existing.ContentHash;

            using var tx = _db.BeginTransaction();

            // Update file record
            await Db.ExecuteAsync(_db,
                "UPDATE Files SET Size = @size, MimeType = @mimeType, Name = @name, ContentHash = @hash, UpdatedAt = @now WHERE BucketId = @bucketId AND Path = @path",
                p =>
                {
                    p.AddWithValue("@size", size);
                    p.AddWithValue("@mimeType", mimeType);
                    p.AddWithValue("@name", name);
                    p.AddWithValue("@hash", hash);
                    p.AddWithValue("@now", now);
                    p.AddWithValue("@bucketId", bucketId);
                    p.AddWithValue("@path", path);
                }, tx);

            // Increment new content ref count (if dedup, it already exists)
            if (deduplicated)
            {
                await Db.ExecuteAsync(_db,
                    "UPDATE ContentObjects SET RefCount = RefCount + 1 WHERE Hash = @hash",
                    p => p.AddWithValue("@hash", hash), tx);
            }
            else
            {
                // Insert new content object
                var diskPath = ContentStorageService.ComputeDiskPath(hash);
                await Db.ExecuteAsync(_db,
                    "INSERT INTO ContentObjects (Hash, Size, DiskPath, RefCount, CreatedAt) VALUES (@hash, @size, @diskPath, 1, @now)",
                    p =>
                    {
                        p.AddWithValue("@hash", hash);
                        p.AddWithValue("@size", size);
                        p.AddWithValue("@diskPath", diskPath);
                        p.AddWithValue("@now", now);
                    }, tx);
            }

            // Decrement old content ref count (if hash changed)
            if (oldHash != null && oldHash != hash)
            {
                await Db.ExecuteAsync(_db,
                    "UPDATE ContentObjects SET RefCount = RefCount - 1 WHERE Hash = @oldHash",
                    p => p.AddWithValue("@oldHash", oldHash), tx);
            }

            // Update bucket total size
            await Db.ExecuteAsync(_db,
                "UPDATE Buckets SET TotalSize = MAX(0, TotalSize - @oldSize) + @size, LastUsedAt = @now WHERE Id = @bucketId",
                p =>
                {
                    p.AddWithValue("@oldSize", oldSize);
                    p.AddWithValue("@size", size);
                    p.AddWithValue("@now", now);
                    p.AddWithValue("@bucketId", bucketId);
                }, tx);

            tx.Commit();

            _cache.InvalidateFile(bucketId, path);
            _cache.InvalidateBucket(bucketId);
            _cache.InvalidateStats();

            var updatedFile = ToUploadedFile(existing.Path, name, size, mimeType, existing.ShortCode, hash, deduplicated, existing.CreatedAt, now);
            await _notifications.NotifyFileUpdated(bucketId, updatedFile.ToBucketFile());
            return updatedFile;
        }
        else
        {
            // New file
            var shortCode = IdGenerator.GenerateShortCode();

            using var tx = _db.BeginTransaction();

            // Insert or increment content object
            if (deduplicated)
            {
                await Db.ExecuteAsync(_db,
                    "UPDATE ContentObjects SET RefCount = RefCount + 1 WHERE Hash = @hash",
                    p => p.AddWithValue("@hash", hash), tx);
            }
            else
            {
                var diskPath = ContentStorageService.ComputeDiskPath(hash);
                await Db.ExecuteAsync(_db,
                    "INSERT INTO ContentObjects (Hash, Size, DiskPath, RefCount, CreatedAt) VALUES (@hash, @size, @diskPath, 1, @now)",
                    p =>
                    {
                        p.AddWithValue("@hash", hash);
                        p.AddWithValue("@size", size);
                        p.AddWithValue("@diskPath", diskPath);
                        p.AddWithValue("@now", now);
                    }, tx);
            }

            // Insert file record
            await Db.ExecuteAsync(_db,
                "INSERT INTO Files (BucketId, Path, Name, Size, MimeType, ShortCode, ContentHash, CreatedAt, UpdatedAt) VALUES (@BucketId, @Path, @Name, @Size, @MimeType, @ShortCode, @ContentHash, @CreatedAt, @UpdatedAt)",
                p =>
                {
                    p.AddWithValue("@BucketId", bucketId);
                    p.AddWithValue("@Path", path);
                    p.AddWithValue("@Name", name);
                    p.AddWithValue("@Size", size);
                    p.AddWithValue("@MimeType", mimeType);
                    p.AddWithValue("@ShortCode", shortCode);
                    p.AddWithValue("@ContentHash", hash);
                    p.AddWithValue("@CreatedAt", now);
                    p.AddWithValue("@UpdatedAt", now);
                }, tx);

            // Short URL
            await Db.ExecuteAsync(_db,
                "INSERT INTO ShortUrls (Code, BucketId, FilePath, CreatedAt) VALUES (@Code, @BucketId, @FilePath, @CreatedAt)",
                p =>
                {
                    p.AddWithValue("@Code", shortCode);
                    p.AddWithValue("@BucketId", bucketId);
                    p.AddWithValue("@FilePath", path);
                    p.AddWithValue("@CreatedAt", now);
                }, tx);

            // Update bucket stats
            await Db.ExecuteAsync(_db,
                "UPDATE Buckets SET FileCount = FileCount + 1, TotalSize = TotalSize + @size, LastUsedAt = @now WHERE Id = @bucketId",
                p =>
                {
                    p.AddWithValue("@size", size);
                    p.AddWithValue("@now", now);
                    p.AddWithValue("@bucketId", bucketId);
                }, tx);

            tx.Commit();

            _cache.InvalidateFile(bucketId, path);
            _cache.InvalidateBucket(bucketId);
            _cache.InvalidateStats();

            var createdFile = ToUploadedFile(path, name, size, mimeType, shortCode, hash, deduplicated, now, now);
            await _notifications.NotifyFileCreated(bucketId, createdFile.ToBucketFile());
            return createdFile;
        }
    }
    catch
    {
        // Clean up temp file on failure
        try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { /* best-effort */ }
        throw;
    }
}
```

Add constructor dependency on `ContentStorageService`:

```csharp
private readonly ContentStorageService _contentStorage;

public UploadService(IDbConnection db, FileStorageService storage, ContentStorageService contentStorage,
    INotificationService notifications, ICacheService cache, ILogger<UploadService> logger)
{
    // ... assign _contentStorage = contentStorage;
}
```

Add `ToUploadedFile` helper and a `ToBucketFile()` extension on `UploadedFile`:

```csharp
private static UploadedFile ToUploadedFile(string path, string name, long size, string mimeType,
    string? shortCode, string hash, bool deduplicated, DateTime createdAt, DateTime updatedAt) => new()
{
    Path = path, Name = name, Size = size, MimeType = mimeType,
    ShortCode = shortCode, ShortUrl = shortCode != null ? $"/s/{shortCode}" : null,
    Sha256 = hash, Deduplicated = deduplicated,
    CreatedAt = createdAt, UpdatedAt = updatedAt
};
```

Add extension method on `UploadedFile`:

```csharp
// In src/CarbonFiles.Core/Models/Responses/UploadedFile.cs
public BucketFile ToBucketFile() => new()
{
    Path = Path, Name = Name, Size = Size, MimeType = MimeType,
    ShortCode = ShortCode, ShortUrl = ShortUrl, Sha256 = Sha256,
    CreatedAt = CreatedAt, UpdatedAt = UpdatedAt
};
```

**Step 4: Update IUploadService interface**

```csharp
Task<UploadedFile> StoreFileAsync(string bucketId, string path, Stream content, AuthContext auth, long maxSize = 0, CancellationToken ct = default);
```

**Step 5: Update UploadEndpoints call sites**

In `src/CarbonFiles.Api/Endpoints/UploadEndpoints.cs`, the multipart and stream handlers collect `List<BucketFile>` results. Change to `List<UploadedFile>` and update the `UploadResponse` construction accordingly.

**Step 6: Run tests**

Run: `dotnet test tests/CarbonFiles.Api.Tests -v m`
Expected: All tests pass, including new CAS-specific tests.

**Step 7: Commit**

```
feat: implement CAS upload flow with deduplication

Upload streams to temp, hashes inline, checks ContentObjects for dedup.
New content goes to sharded CAS store. UploadedFile response includes
sha256 and deduplicated fields. Cross-bucket dedup supported.
```

---

### Task 6: CAS Download Flow

Update download to serve files from content-addressed storage, using SHA256 as ETag.

**Files:**
- Modify: `src/CarbonFiles.Api/Endpoints/FileEndpoints.cs`
- Modify: `src/CarbonFiles.Infrastructure/Services/FileService.cs`

**Step 1: Write failing test for SHA256 ETag**

Add to existing `FileEndpointTests`:

```csharp
[Fact]
public async Task DownloadFile_ETagIsSha256Hash()
{
    var client = Fixture.CreateAdminClient();
    var bucketId = await CreateBucketAsync(client);
    var file = await UploadFileAsync(client, bucketId, "test.txt", "hello world");
    var sha256 = file.GetProperty("sha256").GetString();

    var response = await client.GetAsync(
        $"/api/buckets/{bucketId}/files/test.txt/content",
        TestContext.Current.CancellationToken);

    response.Headers.ETag!.Tag.Should().Be($"\"{sha256}\"");
}
```

**Step 2: Run test to verify it fails**

Run: `dotnet test tests/CarbonFiles.Api.Tests --filter "DownloadFile_ETagIsSha256Hash" -v m`
Expected: FAIL — ETag still uses old `"{size}-{ticks}"` format.

**Step 3: Update ServeFileContent to use CAS path and SHA256 ETag**

In `FileEndpoints.cs`, method `ServeFileContent`:

1. The metadata now has `Sha256` populated. Use it for ETag:

```csharp
var etag = $"\"{meta.Sha256}\"";
```

2. For the physical file path, we need to resolve the content hash to a disk path. Add a method to `IFileService` (or inject `ContentStorageService` directly):

Add to `IFileService`:

```csharp
Task<string?> GetContentDiskPathAsync(string bucketId, string path);
```

Implement in `FileService`:

```csharp
public async Task<string?> GetContentDiskPathAsync(string bucketId, string path)
{
    var hash = await Db.ExecuteScalarAsync<string?>(_db,
        "SELECT ContentHash FROM Files WHERE BucketId = @bucketId AND Path = @path",
        p =>
        {
            p.AddWithValue("@bucketId", bucketId);
            p.AddWithValue("@path", path);
        });
    if (hash == null) return null;

    var diskPath = await Db.ExecuteScalarAsync<string?>(_db,
        "SELECT DiskPath FROM ContentObjects WHERE Hash = @hash",
        p => p.AddWithValue("@hash", hash));

    return diskPath;
}
```

3. In `ServeFileContent`, replace `storageService.GetFilePath(id, actualPath)` with:

```csharp
var diskPath = await fileService.GetContentDiskPathAsync(id, actualPath);
if (diskPath == null) return Results.NotFound();
var physicalPath = contentStorageService.GetFullPath(diskPath);
if (!File.Exists(physicalPath)) return Results.NotFound();
```

Inject `ContentStorageService` into the endpoint method.

**Step 4: Handle fallback for files without ContentHash (pre-migration)**

If `meta.Sha256` is null (pre-migration file), fall back to old path resolution:

```csharp
string physicalPath;
string etag;
if (meta.Sha256 != null)
{
    var diskPath = await fileService.GetContentDiskPathAsync(id, actualPath);
    if (diskPath == null) return Results.NotFound();
    physicalPath = contentStorageService.GetFullPath(diskPath);
    etag = $"\"{meta.Sha256}\"";
}
else
{
    physicalPath = storageService.GetFilePath(id, actualPath);
    etag = $"\"{meta.Size}-{meta.UpdatedAt.Ticks}\"";
}
```

**Step 5: Run all tests**

Run: `dotnet test tests/CarbonFiles.Api.Tests -v m`
Expected: All pass. Existing ETag tests may need updating if they check ETag format — update them to accept SHA256.

**Step 6: Commit**

```
feat: serve downloads from CAS path with SHA256 ETag

Downloads resolve ContentHash → DiskPath → physical file.
ETag is now content-based SHA256 hash. Fallback for pre-migration files.
```

---

### Task 7: CAS Delete Flow

Update delete to decrement ref count instead of deleting content directly.

**Files:**
- Modify: `src/CarbonFiles.Infrastructure/Services/FileService.cs`
- Modify: `src/CarbonFiles.Infrastructure/Services/BucketService.cs`
- Test: `tests/CarbonFiles.Api.Tests/Endpoints/UploadEndpointTests.cs`

**Step 1: Write failing tests for ref-count behavior**

Add to `UploadEndpointTests`:

```csharp
[Fact]
public async Task Delete_DecreasesRefCount_ContentSurvivesIfShared()
{
    var client = Fixture.CreateAdminClient();
    var bucketId = await CreateBucketAsync(client);

    // Upload same content twice with different names
    await UploadFileAsync(client, bucketId, "file1.txt", "shared content");
    await UploadFileAsync(client, bucketId, "file2.txt", "shared content");

    // Delete one
    var deleteResponse = await client.DeleteAsync(
        $"/api/buckets/{bucketId}/files/file1.txt",
        TestContext.Current.CancellationToken);
    deleteResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

    // Other file should still be downloadable
    var downloadResponse = await client.GetAsync(
        $"/api/buckets/{bucketId}/files/file2.txt/content",
        TestContext.Current.CancellationToken);
    downloadResponse.StatusCode.Should().Be(HttpStatusCode.OK);
    var body = await downloadResponse.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
    body.Should().Be("shared content");
}

[Fact]
public async Task Delete_LastReference_ContentStillAccessibleUntilCleanup()
{
    var client = Fixture.CreateAdminClient();
    var bucket1 = await CreateBucketAsync(client);
    var bucket2 = await CreateBucketAsync(client);

    // Upload same content to two buckets
    await UploadFileAsync(client, bucket1, "file.txt", "unique content for delete test");
    var file2 = await UploadFileAsync(client, bucket2, "file.txt", "unique content for delete test");

    // Delete from bucket1
    await client.DeleteAsync($"/api/buckets/{bucket1}/files/file.txt",
        TestContext.Current.CancellationToken);

    // bucket2's copy should still work
    var response = await client.GetAsync($"/api/buckets/{bucket2}/files/file.txt/content",
        TestContext.Current.CancellationToken);
    response.StatusCode.Should().Be(HttpStatusCode.OK);
}
```

**Step 2: Run tests to verify they fail**

Run: `dotnet test tests/CarbonFiles.Api.Tests --filter "Delete_DecreasesRefCount|Delete_LastReference" -v m`
Expected: FAIL — current delete removes the physical file directly.

**Step 3: Update FileService.DeleteAsync for CAS**

In `FileService.DeleteAsync`, replace the disk delete with ref-count decrement:

```csharp
// Inside the transaction, after deleting Files row:
if (entity.ContentHash != null)
{
    await Db.ExecuteAsync(_db,
        "UPDATE ContentObjects SET RefCount = RefCount - 1 WHERE Hash = @hash",
        p => p.AddWithValue("@hash", entity.ContentHash), tx);
}

// After transaction commit — remove old per-bucket disk file if it exists (migration transitional)
if (entity.ContentHash == null)
{
    _storage.DeleteFile(bucketId, path);
}
// Don't delete CAS content — orphan cleanup handles it
```

**Step 4: Update BucketService.DeleteAsync for CAS**

In `BucketService.DeleteAsync`, when deleting all files in a bucket, decrement ref counts:

```csharp
// Before deleting Files rows, decrement all content ref counts
await Db.ExecuteAsync(_db,
    "UPDATE ContentObjects SET RefCount = RefCount - 1 WHERE Hash IN (SELECT ContentHash FROM Files WHERE BucketId = @bucketId AND ContentHash IS NOT NULL)",
    p => p.AddWithValue("@bucketId", bucketId), tx);
```

Don't delete the CAS content directory — orphan cleanup handles that. Still delete the old per-bucket directory for transitional files.

**Step 5: Run all tests**

Run: `dotnet test tests/CarbonFiles.Api.Tests -v m`
Expected: All pass.

**Step 6: Commit**

```
feat: CAS-aware delete with ref-count decrement

Delete decrements ContentObjects.RefCount instead of removing disk file.
Content persists when shared across files/buckets. Orphan cleanup handles
actual disk deletion (Task 8).
```

---

### Task 8: Orphan Content Cleanup

Add orphan content purge to the existing `CleanupService`.

**Files:**
- Modify: `src/CarbonFiles.Infrastructure/Services/CleanupService.cs`
- Modify: `src/CarbonFiles.Infrastructure/Services/CleanupRepository.cs`

**Step 1: Add cleanup repository methods**

In `CleanupRepository.cs`, add:

```csharp
public async Task<List<ContentObjectEntity>> GetOrphanedContentAsync(DateTime olderThan, CancellationToken ct)
{
    return await Db.QueryAsync(_db,
        "SELECT * FROM ContentObjects WHERE RefCount <= 0 AND CreatedAt < @olderThan",
        p => p.AddWithValue("@olderThan", olderThan),
        ContentObjectEntity.Read, ct: ct);
}

public async Task DeleteContentObjectAsync(string hash, CancellationToken ct)
{
    await Db.ExecuteAsync(_db,
        "DELETE FROM ContentObjects WHERE Hash = @hash AND RefCount <= 0",
        p => p.AddWithValue("@hash", hash), ct: ct);
}
```

**Step 2: Add orphan cleanup step to CleanupService**

In `CleanupService.ExecuteAsync`, add a call after `CleanupExpiredBucketsAsync`:

```csharp
await CleanupOrphanedContentAsync(stoppingToken);
```

Implement:

```csharp
internal async Task CleanupOrphanedContentAsync(CancellationToken ct)
{
    using var scope = _provider.CreateScope();
    var repo = scope.ServiceProvider.GetRequiredService<CleanupRepository>();
    var contentStorage = scope.ServiceProvider.GetRequiredService<ContentStorageService>();

    var cutoff = DateTime.UtcNow.AddHours(-1);
    var orphans = await repo.GetOrphanedContentAsync(cutoff, ct);

    if (orphans.Count == 0) return;

    _logger.LogInformation("Cleaning up {Count} orphaned content objects", orphans.Count);

    foreach (var orphan in orphans)
    {
        contentStorage.Delete(orphan.DiskPath);
        await repo.DeleteContentObjectAsync(orphan.Hash, ct);
    }

    _logger.LogInformation("Cleaned up {Count} orphaned content objects", orphans.Count);
}
```

**Step 3: Build and run tests**

Run: `dotnet test tests/CarbonFiles.Api.Tests -v m`
Expected: All pass.

**Step 4: Commit**

```
feat: add orphan content cleanup to CleanupService

Purges ContentObjects with RefCount <= 0 older than 1 hour.
Grace period prevents race with concurrent uploads. Runs in
existing cleanup cycle.
```

---

### Task 9: Unified /files Endpoint

Replace `/ls` with tree mode on `/files`. Add `delimiter`, `prefix`, `cursor` query params.

**Files:**
- Modify: `src/CarbonFiles.Api/Endpoints/FileEndpoints.cs`
- Modify: `src/CarbonFiles.Infrastructure/Services/FileService.cs`
- Modify: `src/CarbonFiles.Core/Interfaces/IFileService.cs`
- Delete: `src/CarbonFiles.Core/Models/Responses/DirectoryListingResponse.cs` (after migration)
- Test: `tests/CarbonFiles.Api.Tests/Endpoints/FileEndpointTests.cs`

**Step 1: Write failing tests for tree mode**

Add to `FileEndpointTests`:

```csharp
[Fact]
public async Task ListFiles_WithDelimiter_ReturnsTreeStructure()
{
    var client = Fixture.CreateAdminClient();
    var bucketId = await CreateBucketAsync(client);

    // Upload files with nested paths
    await UploadFileAsync(client, bucketId, "readme.md", "root file");
    await UploadFileAsync(client, bucketId, "src/main.cs", "main");
    await UploadFileAsync(client, bucketId, "src/utils/helper.cs", "helper");
    await UploadFileAsync(client, bucketId, "src/utils/other.cs", "other");
    await UploadFileAsync(client, bucketId, "docs/guide.md", "guide");

    var response = await client.GetAsync(
        $"/api/buckets/{bucketId}/files?delimiter=/",
        TestContext.Current.CancellationToken);
    response.StatusCode.Should().Be(HttpStatusCode.OK);
    var json = await ParseJsonAsync(response);

    // Root level: readme.md file, src/ and docs/ directories
    json.GetProperty("files").GetArrayLength().Should().Be(1);
    json.GetProperty("files")[0].GetProperty("path").GetString().Should().Be("readme.md");
    json.GetProperty("directories").GetArrayLength().Should().Be(2);
    json.GetProperty("delimiter").GetString().Should().Be("/");
}

[Fact]
public async Task ListFiles_WithDelimiterAndPrefix_ReturnsScoped()
{
    var client = Fixture.CreateAdminClient();
    var bucketId = await CreateBucketAsync(client);

    await UploadFileAsync(client, bucketId, "src/main.cs", "main");
    await UploadFileAsync(client, bucketId, "src/utils/helper.cs", "helper");
    await UploadFileAsync(client, bucketId, "src/utils/other.cs", "other");

    var response = await client.GetAsync(
        $"/api/buckets/{bucketId}/files?delimiter=/&prefix=src/",
        TestContext.Current.CancellationToken);
    response.StatusCode.Should().Be(HttpStatusCode.OK);
    var json = await ParseJsonAsync(response);

    json.GetProperty("prefix").GetString().Should().Be("src/");
    json.GetProperty("files").GetArrayLength().Should().Be(1); // src/main.cs
    json.GetProperty("directories").GetArrayLength().Should().Be(1); // src/utils/
}

[Fact]
public async Task ListFiles_WithDelimiterDeepPrefix_ReturnsLeafFiles()
{
    var client = Fixture.CreateAdminClient();
    var bucketId = await CreateBucketAsync(client);

    await UploadFileAsync(client, bucketId, "src/utils/helper.cs", "helper");
    await UploadFileAsync(client, bucketId, "src/utils/other.cs", "other");

    var response = await client.GetAsync(
        $"/api/buckets/{bucketId}/files?delimiter=/&prefix=src/utils/",
        TestContext.Current.CancellationToken);
    var json = await ParseJsonAsync(response);

    json.GetProperty("files").GetArrayLength().Should().Be(2);
    json.GetProperty("directories").GetArrayLength().Should().Be(0);
}

[Fact]
public async Task ListFiles_NoDelimiter_ReturnsFlatList()
{
    var client = Fixture.CreateAdminClient();
    var bucketId = await CreateBucketAsync(client);

    await UploadFileAsync(client, bucketId, "src/main.cs", "main");
    await UploadFileAsync(client, bucketId, "docs/guide.md", "guide");

    var response = await client.GetAsync(
        $"/api/buckets/{bucketId}/files",
        TestContext.Current.CancellationToken);
    var json = await ParseJsonAsync(response);

    // Flat list — should have items array, not directories
    json.GetProperty("items").GetArrayLength().Should().Be(2);
}

[Fact]
public async Task ListFiles_TreeMode_DirectoriesHaveStats()
{
    var client = Fixture.CreateAdminClient();
    var bucketId = await CreateBucketAsync(client);

    await UploadFileAsync(client, bucketId, "src/a.txt", "aaa");
    await UploadFileAsync(client, bucketId, "src/b.txt", "bbb");

    var response = await client.GetAsync(
        $"/api/buckets/{bucketId}/files?delimiter=/",
        TestContext.Current.CancellationToken);
    var json = await ParseJsonAsync(response);

    var dir = json.GetProperty("directories")[0];
    dir.GetProperty("path").GetString().Should().Be("src/");
    dir.GetProperty("file_count").GetInt32().Should().Be(2);
    dir.GetProperty("total_size").GetInt64().Should().BeGreaterThan(0);
}

[Fact]
public async Task ListFiles_TreeModeCursorPagination()
{
    var client = Fixture.CreateAdminClient();
    var bucketId = await CreateBucketAsync(client);

    // Upload enough files for pagination
    for (int i = 0; i < 5; i++)
        await UploadFileAsync(client, bucketId, $"file{i:D2}.txt", $"content{i}");

    var response = await client.GetAsync(
        $"/api/buckets/{bucketId}/files?delimiter=/&limit=2",
        TestContext.Current.CancellationToken);
    var json = await ParseJsonAsync(response);

    json.GetProperty("files").GetArrayLength().Should().Be(2);
    var cursor = json.GetProperty("cursor").GetString();
    cursor.Should().NotBeNull();

    // Fetch next page
    var response2 = await client.GetAsync(
        $"/api/buckets/{bucketId}/files?delimiter=/&limit=2&cursor={cursor}",
        TestContext.Current.CancellationToken);
    var json2 = await ParseJsonAsync(response2);

    json2.GetProperty("files").GetArrayLength().Should().Be(2);
}
```

**Step 2: Run tests to verify they fail**

Run: `dotnet test tests/CarbonFiles.Api.Tests --filter "ListFiles_WithDelimiter|ListFiles_NoDelimiter|ListFiles_TreeMode" -v m`
Expected: FAIL.

**Step 3: Add ListTreeAsync to FileService**

In `IFileService`, add:

```csharp
Task<FileTreeResponse> ListTreeAsync(string bucketId, string? prefix, string delimiter, int limit, string? cursor);
```

Implement in `FileService`:

```csharp
public async Task<FileTreeResponse> ListTreeAsync(string bucketId, string? prefix, string delimiter, int limit, string? cursor)
{
    prefix ??= "";

    // Range scan: get all paths with prefix, ordered by path
    // We fetch more than limit to find directories too
    var sql = cursor != null
        ? "SELECT Path, Size FROM Files WHERE BucketId = @bucketId AND Path >= @cursor AND Path LIKE @likePrefix ORDER BY Path LIMIT @fetchLimit"
        : "SELECT Path, Size FROM Files WHERE BucketId = @bucketId AND Path LIKE @likePrefix ORDER BY Path LIMIT @fetchLimit";

    var likePrefix = prefix.Replace("%", "[%]").Replace("_", "[_]") + "%";
    var fetchLimit = limit * 10 + 100; // Overfetch to compute directories

    var allEntries = await Db.QueryAsync(_db, sql,
        p =>
        {
            p.AddWithValue("@bucketId", bucketId);
            p.AddWithValue("@likePrefix", likePrefix);
            p.AddWithValue("@fetchLimit", fetchLimit);
            if (cursor != null)
                p.AddWithValue("@cursor", cursor);
        },
        r => (Path: r.GetString(0), Size: r.GetInt64(1)));

    // Partition into files at this level vs subdirectories
    var files = new List<string>();
    var dirStats = new Dictionary<string, (int Count, long Size)>();

    foreach (var (path, size) in allEntries)
    {
        var remainder = path[prefix.Length..];
        var delimIndex = remainder.IndexOf(delimiter, StringComparison.Ordinal);

        if (delimIndex < 0)
        {
            // Direct file at this level
            files.Add(path);
        }
        else
        {
            // File is inside a subdirectory
            var dirName = prefix + remainder[..(delimIndex + delimiter.Length)];
            if (dirStats.TryGetValue(dirName, out var stats))
                dirStats[dirName] = (stats.Count + 1, stats.Size + size);
            else
                dirStats[dirName] = (1, size);
        }
    }

    // Fetch full file entities for files at this level (up to limit)
    var filePaths = files.Take(limit).ToList();
    var fileEntities = new List<BucketFile>();
    if (filePaths.Count > 0)
    {
        // Fetch each file's full metadata
        foreach (var fp in filePaths)
        {
            var entity = await Db.QueryFirstOrDefaultAsync(_db,
                "SELECT * FROM Files WHERE BucketId = @bucketId AND Path = @path",
                p =>
                {
                    p.AddWithValue("@bucketId", bucketId);
                    p.AddWithValue("@path", fp);
                },
                FileEntity.Read);
            if (entity != null)
                fileEntities.Add(entity.ToBucketFile());
        }
    }

    var directories = dirStats
        .OrderBy(d => d.Key)
        .Select(d => new DirectoryEntry { Path = d.Key, FileCount = d.Value.Count, TotalSize = d.Value.Size })
        .ToList();

    // Cursor: last file path if we hit the limit
    string? nextCursor = null;
    if (files.Count > limit || allEntries.Count >= fetchLimit)
        nextCursor = filePaths.LastOrDefault() ?? allEntries.LastOrDefault().Path;

    return new FileTreeResponse
    {
        Prefix = string.IsNullOrEmpty(prefix) ? null : prefix,
        Delimiter = delimiter,
        Directories = directories,
        Files = fileEntities,
        TotalFiles = files.Count,
        TotalDirectories = directories.Count,
        Cursor = nextCursor,
    };
}
```

**Step 4: Update FileEndpoints to support tree mode**

In `FileEndpoints.cs`, modify the `GET /api/buckets/{id}/files` handler:

```csharp
group.MapGet("/{id}/files", async (
    string id,
    IFileService fileService,
    IBucketService bucketService,
    string? delimiter,
    string? prefix,
    string? cursor,
    int? limit,
    int? offset,
    string? sort,
    string? order) =>
{
    // Verify bucket exists
    var bucket = await bucketService.GetBucketAsync(id);
    if (bucket == null) return Results.NotFound();

    if (delimiter != null)
    {
        // Tree mode
        var treeLimit = Math.Clamp(limit ?? 100, 1, 1000);
        var result = await fileService.ListTreeAsync(id, prefix, delimiter, treeLimit, cursor);
        return Results.Ok(result);
    }
    else
    {
        // Flat mode (existing behavior)
        var pagination = new PaginationParams
        {
            Limit = Math.Clamp(limit ?? 50, 1, 1000),
            Offset = Math.Max(offset ?? 0, 0),
            Sort = sort,
            Order = order
        };
        var result = await fileService.ListAsync(id, pagination);
        return Results.Ok(result);
    }
});
```

**Step 5: Remove /ls endpoint**

In `FileEndpoints.cs`, remove the `/ls` route mapping and its handler. Also remove `ListDirectoryAsync` from `IFileService` and `FileService`. Remove `DirectoryListingResponse` from the serialization context. Delete `DirectoryListingResponse.cs`.

**Step 6: Run all tests**

Run: `dotnet test tests/CarbonFiles.Api.Tests -v m`
Expected: All pass. Any tests that used `/ls` need to be removed or rewritten to use `/files?delimiter=/`.

**Step 7: Commit**

```
feat: unified /files endpoint with tree browsing

GET /files?delimiter=/&prefix=src/ returns directories + files.
GET /files without delimiter returns flat paginated list.
Cursor-based pagination for tree mode. /ls endpoint removed.
Directory entries include file_count and total_size.
```

---

### Task 10: Bucket Detail Changes

Update `GET /api/buckets/{id}` — default: no files, add dedup stats, `?include=files` for compat.

**Files:**
- Modify: `src/CarbonFiles.Infrastructure/Services/BucketService.cs`
- Modify: `src/CarbonFiles.Core/Interfaces/IBucketService.cs`
- Test: `tests/CarbonFiles.Api.Tests/Endpoints/BucketEndpointTests.cs`

**Step 1: Write failing tests**

Add to `BucketEndpointTests`:

```csharp
[Fact]
public async Task GetBucket_Default_NoFilesArray()
{
    var client = Fixture.CreateAdminClient();
    var response = await client.PostAsJsonAsync("/api/buckets",
        new { name = "test" }, TestContext.Current.CancellationToken);
    var json = await ParseJsonAsync(response);
    var id = json.GetProperty("id").GetString()!;

    var getResponse = await client.GetAsync($"/api/buckets/{id}",
        TestContext.Current.CancellationToken);
    var detail = await ParseJsonAsync(getResponse);

    detail.TryGetProperty("files", out _).Should().BeFalse();
    detail.GetProperty("unique_content_count").GetInt32().Should().Be(0);
    detail.GetProperty("unique_content_size").GetInt64().Should().Be(0);
}

[Fact]
public async Task GetBucket_IncludeFiles_ReturnsFilesArray()
{
    var client = Fixture.CreateAdminClient();
    var response = await client.PostAsJsonAsync("/api/buckets",
        new { name = "test" }, TestContext.Current.CancellationToken);
    var json = await ParseJsonAsync(response);
    var id = json.GetProperty("id").GetString()!;

    var getResponse = await client.GetAsync($"/api/buckets/{id}?include=files",
        TestContext.Current.CancellationToken);
    var detail = await ParseJsonAsync(getResponse);

    detail.TryGetProperty("files", out _).Should().BeTrue();
}

[Fact]
public async Task GetBucket_DedupStats_ReflectSharedContent()
{
    var client = Fixture.CreateAdminClient();
    var response = await client.PostAsJsonAsync("/api/buckets",
        new { name = "test" }, TestContext.Current.CancellationToken);
    var json = await ParseJsonAsync(response);
    var id = json.GetProperty("id").GetString()!;

    // Upload identical content twice
    using var form1 = new MultipartFormDataContent();
    form1.Add(new StringContent("shared"), "file", "a.txt");
    await client.PostAsync($"/api/buckets/{id}/upload", form1, TestContext.Current.CancellationToken);

    using var form2 = new MultipartFormDataContent();
    form2.Add(new StringContent("shared"), "file", "b.txt");
    await client.PostAsync($"/api/buckets/{id}/upload", form2, TestContext.Current.CancellationToken);

    var getResponse = await client.GetAsync($"/api/buckets/{id}",
        TestContext.Current.CancellationToken);
    var detail = await ParseJsonAsync(getResponse);

    detail.GetProperty("file_count").GetInt32().Should().Be(2);
    detail.GetProperty("unique_content_count").GetInt32().Should().Be(1);
}
```

**Step 2: Run tests to verify they fail**

Run: `dotnet test tests/CarbonFiles.Api.Tests --filter "GetBucket_Default_NoFilesArray|GetBucket_IncludeFiles|GetBucket_DedupStats" -v m`
Expected: FAIL.

**Step 3: Update BucketService.GetByIdAsync**

Add `include` parameter. Query dedup stats:

```csharp
public async Task<BucketDetailResponse?> GetByIdAsync(string id, bool includeFiles = false)
{
    // ... existing bucket query ...

    // Dedup stats
    var uniqueContentCount = await Db.ExecuteScalarAsync<int>(_db,
        "SELECT COUNT(DISTINCT ContentHash) FROM Files WHERE BucketId = @id AND ContentHash IS NOT NULL",
        p => p.AddWithValue("@id", id));
    var uniqueContentSize = await Db.ExecuteScalarAsync<long>(_db,
        "SELECT COALESCE(SUM(co.Size), 0) FROM ContentObjects co WHERE co.Hash IN (SELECT DISTINCT ContentHash FROM Files WHERE BucketId = @id AND ContentHash IS NOT NULL)",
        p => p.AddWithValue("@id", id));

    IReadOnlyList<BucketFile>? files = null;
    bool? hasMoreFiles = null;

    if (includeFiles)
    {
        var fileEntities = await Db.QueryAsync(_db,
            "SELECT * FROM Files WHERE BucketId = @id ORDER BY Path LIMIT 101",
            p => p.AddWithValue("@id", id),
            FileEntity.Read);
        hasMoreFiles = fileEntities.Count > 100;
        files = fileEntities.Take(100).Select(f => f.ToBucketFile()).ToList();
    }

    return new BucketDetailResponse
    {
        // ... existing fields ...
        UniqueContentCount = uniqueContentCount,
        UniqueContentSize = uniqueContentSize,
        Files = files,
        HasMoreFiles = hasMoreFiles,
    };
}
```

**Step 4: Update BucketEndpoints to pass include param**

```csharp
group.MapGet("/{id}", async (string id, IBucketService bucketService, string? include) =>
{
    var includeFiles = string.Equals(include, "files", StringComparison.OrdinalIgnoreCase);
    var bucket = await bucketService.GetByIdAsync(id, includeFiles);
    return bucket == null ? Results.NotFound() : Results.Ok(bucket);
});
```

**Step 5: Update existing tests that expect files in bucket detail**

Existing `BucketEndpointTests` that test `GetBucket` probably assert on `files` array. Update them to use `?include=files` where they need the files array, or remove the assertion.

**Step 6: Run all tests**

Run: `dotnet test tests/CarbonFiles.Api.Tests -v m`
Expected: All pass.

**Step 7: Commit**

```
feat: bucket detail without files by default, add dedup stats

GET /api/buckets/{id} no longer returns files array by default.
Use ?include=files for backwards compat. Response now includes
unique_content_count and unique_content_size.
```

---

### Task 11: Verification Endpoint

Add `GET /api/buckets/{id}/files/{*path}/verify` to recompute and verify content hash.

**Files:**
- Modify: `src/CarbonFiles.Api/Endpoints/FileEndpoints.cs`
- Modify: `src/CarbonFiles.Core/Interfaces/IFileService.cs`
- Modify: `src/CarbonFiles.Infrastructure/Services/FileService.cs`
- Test: `tests/CarbonFiles.Api.Tests/Endpoints/FileEndpointTests.cs`

**Step 1: Write failing tests**

```csharp
[Fact]
public async Task VerifyFile_ValidContent_ReturnsValid()
{
    var client = Fixture.CreateAdminClient();
    var bucketId = await CreateBucketAsync(client);
    await UploadFileAsync(client, bucketId, "test.txt", "verify me");

    var response = await client.GetAsync(
        $"/api/buckets/{bucketId}/files/test.txt/verify",
        TestContext.Current.CancellationToken);
    response.StatusCode.Should().Be(HttpStatusCode.OK);
    var json = await ParseJsonAsync(response);

    json.GetProperty("path").GetString().Should().Be("test.txt");
    json.GetProperty("valid").GetBoolean().Should().BeTrue();
    json.GetProperty("stored_hash").GetString()
        .Should().Be(json.GetProperty("computed_hash").GetString());
}

[Fact]
public async Task VerifyFile_NonexistentFile_Returns404()
{
    var client = Fixture.CreateAdminClient();
    var bucketId = await CreateBucketAsync(client);

    var response = await client.GetAsync(
        $"/api/buckets/{bucketId}/files/nope.txt/verify",
        TestContext.Current.CancellationToken);
    response.StatusCode.Should().Be(HttpStatusCode.NotFound);
}
```

**Step 2: Run tests to verify they fail**

Run: `dotnet test tests/CarbonFiles.Api.Tests --filter "VerifyFile" -v m`
Expected: FAIL — endpoint doesn't exist.

**Step 3: Add VerifyAsync to FileService**

In `IFileService`:

```csharp
Task<VerifyResponse?> VerifyAsync(string bucketId, string path);
```

In `FileService`:

```csharp
public async Task<VerifyResponse?> VerifyAsync(string bucketId, string path)
{
    var entity = await Db.QueryFirstOrDefaultAsync(_db,
        "SELECT * FROM Files WHERE BucketId = @bucketId AND Path = @path",
        p =>
        {
            p.AddWithValue("@bucketId", bucketId);
            p.AddWithValue("@path", path);
        },
        FileEntity.Read);
    if (entity?.ContentHash == null)
        return null;

    var contentObj = await Db.QueryFirstOrDefaultAsync(_db,
        "SELECT * FROM ContentObjects WHERE Hash = @hash",
        p => p.AddWithValue("@hash", entity.ContentHash),
        ContentObjectEntity.Read);
    if (contentObj == null)
        return null;

    // Recompute hash from disk
    var fullPath = _contentStorage.GetFullPath(contentObj.DiskPath);
    if (!File.Exists(fullPath))
        return new VerifyResponse
        {
            Path = path,
            StoredHash = entity.ContentHash,
            ComputedHash = "",
            Valid = false
        };

    using var stream = File.OpenRead(fullPath);
    using var sha256 = System.Security.Cryptography.IncrementalHash.CreateHash(
        System.Security.Cryptography.HashAlgorithmName.SHA256);
    var buffer = new byte[81920];
    int bytesRead;
    while ((bytesRead = await stream.ReadAsync(buffer)) > 0)
        sha256.AppendData(buffer.AsSpan(0, bytesRead));
    var computedHash = Convert.ToHexStringLower(sha256.GetHashAndReset());

    return new VerifyResponse
    {
        Path = path,
        StoredHash = entity.ContentHash,
        ComputedHash = computedHash,
        Valid = entity.ContentHash == computedHash
    };
}
```

Add `ContentStorageService` dependency to `FileService` constructor.

**Step 4: Add verify route**

In `FileEndpoints.cs`, the route needs to intercept `{*filePath}` ending in `/verify`. Add before the existing wildcard route:

```csharp
group.MapGet("/{id}/files/{*filePath}", async (string id, string filePath, IFileService fileService, IBucketService bucketService) =>
{
    // ... existing handler that checks for /content suffix ...
    // Add check for /verify suffix:
    if (filePath.EndsWith("/verify", StringComparison.OrdinalIgnoreCase))
    {
        var actualPath = filePath[..^"/verify".Length];
        var result = await fileService.VerifyAsync(id, actualPath);
        return result == null ? Results.NotFound() : Results.Ok(result);
    }
    // ... existing metadata/content logic ...
});
```

**Step 5: Run all tests**

Run: `dotnet test tests/CarbonFiles.Api.Tests -v m`
Expected: All pass.

**Step 6: Commit**

```
feat: add file integrity verification endpoint

GET /api/buckets/{id}/files/{path}/verify recomputes SHA256 from disk
and compares to stored hash. Returns stored_hash, computed_hash, valid.
```

---

### Task 12: PATCH Rewrite for CAS

Rewrite the PATCH endpoint to create new content objects instead of modifying in place.

**Files:**
- Modify: `src/CarbonFiles.Api/Endpoints/FileEndpoints.cs`
- Modify: `src/CarbonFiles.Infrastructure/Services/FileService.cs`
- Modify: `src/CarbonFiles.Core/Interfaces/IFileService.cs`
- Remove: `FileStorageService.PatchFileAsync` (no longer needed after CAS migration)
- Test: `tests/CarbonFiles.Api.Tests/Endpoints/FileEndpointTests.cs`

**Step 1: Write/update PATCH test**

```csharp
[Fact]
public async Task PatchFile_Append_CreatesNewContentObject()
{
    var client = Fixture.CreateAdminClient();
    var bucketId = await CreateBucketAsync(client);
    var original = await UploadFileAsync(client, bucketId, "test.txt", "hello");
    var originalHash = original.GetProperty("sha256").GetString();

    var request = new HttpRequestMessage(HttpMethod.Patch, $"/api/buckets/{bucketId}/files/test.txt");
    request.Content = new StringContent(" world");
    request.Headers.Add("X-Append", "true");
    var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
    response.StatusCode.Should().Be(HttpStatusCode.OK);

    // Verify hash changed (content changed)
    var meta = await client.GetAsync($"/api/buckets/{bucketId}/files/test.txt",
        TestContext.Current.CancellationToken);
    var json = await ParseJsonAsync(meta);
    json.GetProperty("sha256").GetString().Should().NotBe(originalHash);

    // Verify content is correct
    var content = await client.GetStringAsync(
        $"/api/buckets/{bucketId}/files/test.txt/content",
        TestContext.Current.CancellationToken);
    content.Should().Be("hello world");
}
```

**Step 2: Implement CAS-aware PATCH in FileService**

Add to `IFileService`:

```csharp
Task<bool> PatchFileAsync(string bucketId, string path, Stream content, long offset, bool append);
```

In `FileService`:

```csharp
public async Task<bool> PatchFileAsync(string bucketId, string path, Stream patchContent, long offset, bool append)
{
    var entity = await Db.QueryFirstOrDefaultAsync(_db,
        "SELECT * FROM Files WHERE BucketId = @bucketId AND Path = @path",
        p =>
        {
            p.AddWithValue("@bucketId", bucketId);
            p.AddWithValue("@path", path);
        },
        FileEntity.Read);
    if (entity?.ContentHash == null) return false;

    var contentObj = await Db.QueryFirstOrDefaultAsync(_db,
        "SELECT * FROM ContentObjects WHERE Hash = @hash",
        p => p.AddWithValue("@hash", entity.ContentHash),
        ContentObjectEntity.Read);
    if (contentObj == null) return false;

    // Read original content, apply patch, write to new temp, hash result
    var originalPath = _contentStorage.GetFullPath(contentObj.DiskPath);
    var (tempPath, newSize, newHash) = await ApplyPatchToTempAsync(originalPath, patchContent, offset, append);

    try
    {
        var now = DateTime.UtcNow;
        var oldHash = entity.ContentHash;

        using var tx = _db.BeginTransaction();

        // Check if new content already exists
        var existingNew = await Db.QueryFirstOrDefaultAsync(_db,
            "SELECT * FROM ContentObjects WHERE Hash = @hash",
            p => p.AddWithValue("@hash", newHash),
            ContentObjectEntity.Read);

        if (existingNew != null)
        {
            File.Delete(tempPath);
            await Db.ExecuteAsync(_db,
                "UPDATE ContentObjects SET RefCount = RefCount + 1 WHERE Hash = @hash",
                p => p.AddWithValue("@hash", newHash), tx);
        }
        else
        {
            var diskPath = ContentStorageService.ComputeDiskPath(newHash);
            _contentStorage.MoveToContentStore(tempPath, diskPath);
            await Db.ExecuteAsync(_db,
                "INSERT INTO ContentObjects (Hash, Size, DiskPath, RefCount, CreatedAt) VALUES (@hash, @size, @diskPath, 1, @now)",
                p =>
                {
                    p.AddWithValue("@hash", newHash);
                    p.AddWithValue("@size", newSize);
                    p.AddWithValue("@diskPath", diskPath);
                    p.AddWithValue("@now", now);
                }, tx);
        }

        // Decrement old content ref
        await Db.ExecuteAsync(_db,
            "UPDATE ContentObjects SET RefCount = RefCount - 1 WHERE Hash = @oldHash",
            p => p.AddWithValue("@oldHash", oldHash), tx);

        // Update file record
        await Db.ExecuteAsync(_db,
            "UPDATE Files SET Size = @size, ContentHash = @hash, UpdatedAt = @now WHERE BucketId = @bucketId AND Path = @path",
            p =>
            {
                p.AddWithValue("@size", newSize);
                p.AddWithValue("@hash", newHash);
                p.AddWithValue("@now", now);
                p.AddWithValue("@bucketId", bucketId);
                p.AddWithValue("@path", path);
            }, tx);

        // Update bucket size
        await Db.ExecuteAsync(_db,
            "UPDATE Buckets SET TotalSize = MAX(0, TotalSize - @oldSize + @newSize) WHERE Id = @bucketId",
            p =>
            {
                p.AddWithValue("@oldSize", entity.Size);
                p.AddWithValue("@newSize", newSize);
                p.AddWithValue("@bucketId", bucketId);
            }, tx);

        tx.Commit();

        _cache.InvalidateFile(bucketId, path);
        _cache.InvalidateBucket(bucketId);
        _cache.InvalidateStats();

        return true;
    }
    catch
    {
        try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { }
        throw;
    }
}

private async Task<(string TempPath, long Size, string Hash)> ApplyPatchToTempAsync(
    string originalPath, Stream patchContent, long offset, bool append)
{
    var tempDir = Path.Combine(Path.GetDirectoryName(originalPath)!, "..", "..", "tmp");
    Directory.CreateDirectory(tempDir);
    var tempPath = Path.Combine(tempDir, $"{Guid.NewGuid():N}.tmp");

    using var sha256 = System.Security.Cryptography.IncrementalHash.CreateHash(
        System.Security.Cryptography.HashAlgorithmName.SHA256);

    await using var outFile = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920);
    await using var inFile = new FileStream(originalPath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920);

    var buffer = new byte[81920];
    long totalBytes = 0;

    if (append)
    {
        // Copy all original content
        int read;
        while ((read = await inFile.ReadAsync(buffer)) > 0)
        {
            sha256.AppendData(buffer.AsSpan(0, read));
            await outFile.WriteAsync(buffer.AsMemory(0, read));
            totalBytes += read;
        }
        // Append patch content
        while ((read = await patchContent.ReadAsync(buffer)) > 0)
        {
            sha256.AppendData(buffer.AsSpan(0, read));
            await outFile.WriteAsync(buffer.AsMemory(0, read));
            totalBytes += read;
        }
    }
    else
    {
        // Copy up to offset
        long copied = 0;
        while (copied < offset)
        {
            var toRead = (int)Math.Min(buffer.Length, offset - copied);
            var read = await inFile.ReadAsync(buffer.AsMemory(0, toRead));
            if (read == 0) break;
            sha256.AppendData(buffer.AsSpan(0, read));
            await outFile.WriteAsync(buffer.AsMemory(0, read));
            copied += read;
            totalBytes += read;
        }
        // Write patch content
        int patchRead;
        while ((patchRead = await patchContent.ReadAsync(buffer)) > 0)
        {
            sha256.AppendData(buffer.AsSpan(0, patchRead));
            await outFile.WriteAsync(buffer.AsMemory(0, patchRead));
            totalBytes += patchRead;
        }
        // Copy remainder of original after patch
        var patchLength = totalBytes - copied;
        inFile.Seek(offset + patchLength, SeekOrigin.Begin);
        int tailRead;
        while ((tailRead = await inFile.ReadAsync(buffer)) > 0)
        {
            sha256.AppendData(buffer.AsSpan(0, tailRead));
            await outFile.WriteAsync(buffer.AsMemory(0, tailRead));
            totalBytes += tailRead;
        }
    }

    var hashHex = Convert.ToHexStringLower(sha256.GetHashAndReset());
    return (tempPath, totalBytes, hashHex);
}
```

**Step 3: Update PATCH endpoint in FileEndpoints.cs**

Replace the existing `PatchFileAsync` call with `fileService.PatchFileAsync(...)`.

**Step 4: Run all tests**

Run: `dotnet test tests/CarbonFiles.Api.Tests -v m`
Expected: All pass.

**Step 5: Commit**

```
feat: CAS-aware PATCH creates new content objects

PATCH reads original from CAS, applies modification, hashes result,
stores as new content. Old content ref decremented. Full CAS integrity.
```

---

### Task 13: Apply PathNormalizer to Uploads

Wire PathNormalizer into the upload flow.

**Files:**
- Modify: `src/CarbonFiles.Api/Endpoints/UploadEndpoints.cs`

**Step 1: Write failing test**

Add to `UploadEndpointTests`:

```csharp
[Fact]
public async Task Upload_NormalizesPath()
{
    var client = Fixture.CreateAdminClient();
    var bucketId = await CreateBucketAsync(client);

    using var form = new MultipartFormDataContent();
    form.Add(new StringContent("content"), "src\\utils\\helper.cs", "helper.cs");
    var response = await client.PostAsync($"/api/buckets/{bucketId}/upload", form,
        TestContext.Current.CancellationToken);
    response.EnsureSuccessStatusCode();
    var json = await JsonDocument.ParseAsync(
        await response.Content.ReadAsStreamAsync(TestContext.Current.CancellationToken),
        cancellationToken: TestContext.Current.CancellationToken);
    var path = json.RootElement.GetProperty("uploaded")[0].GetProperty("path").GetString();

    // Backslashes should be converted to forward slashes
    path.Should().Be("src/utils/helper.cs");
}

[Fact]
public async Task Upload_PathTraversal_Returns400()
{
    var client = Fixture.CreateAdminClient();
    var bucketId = await CreateBucketAsync(client);

    using var form = new MultipartFormDataContent();
    form.Add(new StringContent("content"), "../etc/passwd", "passwd");
    var response = await client.PostAsync($"/api/buckets/{bucketId}/upload", form,
        TestContext.Current.CancellationToken);
    response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
}
```

**Step 2: Add PathNormalizer calls in UploadEndpoints**

Before calling `StoreFileAsync`, normalize the path:

```csharp
try
{
    path = PathNormalizer.Normalize(path);
}
catch (ArgumentException)
{
    return Results.BadRequest(new ErrorResponse { Error = "Invalid file path" });
}
```

Apply to both multipart and stream upload handlers.

**Step 3: Run all tests**

Run: `dotnet test tests/CarbonFiles.Api.Tests -v m`
Expected: All pass.

**Step 4: Commit**

```
feat: normalize file paths on upload

PathNormalizer applied to all upload paths. Backslash conversion,
traversal rejection, empty component rejection. Returns 400 on invalid.
```

---

### Task 14: Update FileEntity.ToBucketFile and Related Mappings

Ensure all places that convert `FileEntity` → `BucketFile` include the `Sha256` field.

**Files:**
- Modify: `src/CarbonFiles.Infrastructure/Data/Entities/FileEntity.cs` (or wherever `ToBucketFile` is defined)
- Modify: `src/CarbonFiles.Infrastructure/Services/BucketService.cs` (file listing in `GetByIdAsync`, `GetAllFilesAsync`)

**Step 1: Find and update ToBucketFile**

Search for `ToBucketFile` across the codebase. Update every mapping to include:

```csharp
Sha256 = ContentHash,
```

**Step 2: Update GetMetadataAsync cache**

The file metadata cache stores `BucketFile`. Ensure `Sha256` is populated when caching.

**Step 3: Run all tests**

Run: `dotnet test tests/CarbonFiles.Api.Tests -v m`
Expected: All pass.

**Step 4: Commit**

```
fix: include Sha256 in all BucketFile mappings

Ensure ContentHash → Sha256 is mapped in ToBucketFile, metadata
cache, and all file listing queries.
```

---

### Task 15: Migration Logic in Migrator

Add migration to compute hashes for existing files and move them to CAS layout.

**Files:**
- Modify: `src/CarbonFiles.Migrator/Program.cs` (or equivalent entry point)

**Step 1: Explore Migrator project structure**

Read the Migrator project to understand its current structure and entry point.

**Step 2: Add CAS migration logic**

After schema initialization, add a migration step:

```csharp
// Pseudo-code for the migration
var unmigrated = query("SELECT f.BucketId, f.Path, f.Size FROM Files f WHERE f.ContentHash IS NULL");

foreach (var file in unmigrated)
{
    var oldPath = GetOldFilePath(file.BucketId, file.Path);
    if (!File.Exists(oldPath)) { log warning; continue; }

    // Compute SHA256
    using var stream = File.OpenRead(oldPath);
    var hash = ComputeSha256(stream);
    var diskPath = ComputeDiskPath(hash);
    var fullContentPath = Path.Combine(contentDir, diskPath);

    // Check if content already exists (dedup during migration!)
    if (existsInContentObjects(hash))
    {
        incrementRefCount(hash);
        File.Delete(oldPath); // Remove old copy
    }
    else
    {
        Directory.CreateDirectory(Path.GetDirectoryName(fullContentPath));
        File.Move(oldPath, fullContentPath);
        insertContentObject(hash, file.Size, diskPath);
    }

    updateFileContentHash(file.BucketId, file.Path, hash);
}
```

**Step 3: Build and test with a manual migration scenario**

Run: `dotnet build src/CarbonFiles.Migrator`
Expected: Build succeeds.

**Step 4: Commit**

```
feat: add CAS migration to Migrator

Computes SHA256 for existing files, moves to content-addressed layout,
populates ContentObjects table, deduplicates during migration.
```

---

### Task 16: Fix Existing Tests

Update existing tests broken by schema and response changes.

**Files:**
- Modify: `tests/CarbonFiles.Api.Tests/Endpoints/FileEndpointTests.cs`
- Modify: `tests/CarbonFiles.Api.Tests/Endpoints/BucketEndpointTests.cs`
- Modify: `tests/CarbonFiles.Api.Tests/TestFixture.cs` (if schema changes require it)

**Step 1: Update ETag assertions**

Tests that check ETag format need to accept SHA256 hashes instead of `"{size}-{ticks}"`.

**Step 2: Update bucket detail tests**

Tests that assert on `files` array in bucket detail need `?include=files`.

**Step 3: Remove /ls tests if any**

Remove or convert any tests hitting the old `/ls` endpoint.

**Step 4: Run full test suite**

Run: `dotnet test tests/CarbonFiles.Api.Tests -v m`
Expected: All pass.

**Step 5: Commit**

```
test: update existing tests for CAS and unified /files

ETag assertions updated for SHA256, bucket detail tests use
?include=files, /ls tests converted to /files?delimiter=/.
```

---

### Task 17: Final Verification and Cleanup

**Step 1: Run full test suite**

Run: `dotnet test -v m`
Expected: All pass.

**Step 2: Build in Release mode**

Run: `dotnet build -c Release`
Expected: Success with no errors.

**Step 3: Remove old FileStorageService methods no longer needed**

- `PatchFileAsync` can be removed if PATCH now goes through `FileService.PatchFileAsync`.
- `GetFilePath`, `OpenRead`, `DeleteFile` may still be needed for pre-migration fallback. Keep for now.

**Step 4: Clean up unused imports and dead code**

**Step 5: Commit**

```
chore: cleanup unused code and final verification

Remove dead methods, clean imports. All tests pass, release build clean.
```
