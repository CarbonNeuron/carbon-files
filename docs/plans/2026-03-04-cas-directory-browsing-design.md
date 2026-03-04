# Content-Addressable Storage + Directory Tree Browsing

**Date:** 2026-03-04
**Status:** Approved

## Overview

Two major changes to CarbonFiles:

1. **Content-addressable storage (CAS)** — files stored by SHA256 hash, enabling deduplication and integrity verification
2. **Unified directory browsing** — merge flat listing and tree browsing into a single `/files` endpoint, remove `/ls`

## Database Schema Changes

### New table: ContentObjects

```sql
CREATE TABLE IF NOT EXISTS "ContentObjects" (
    "Hash" TEXT NOT NULL PRIMARY KEY,
    "Size" INTEGER NOT NULL,
    "DiskPath" TEXT NOT NULL,
    "RefCount" INTEGER NOT NULL DEFAULT 1,
    "CreatedAt" TEXT NOT NULL
);
CREATE INDEX IF NOT EXISTS "IX_ContentObjects_Orphans"
    ON "ContentObjects" ("RefCount") WHERE "RefCount" = 0;
```

### Modified Files table

Add `ContentHash` column (nullable during migration, treated as required by application code after migration):

```sql
ALTER TABLE "Files" ADD COLUMN "ContentHash" TEXT NULL
    REFERENCES "ContentObjects"("Hash");
CREATE INDEX IF NOT EXISTS "IX_Files_ContentHash" ON "Files" ("ContentHash");
```

## Disk Layout

Content stored under `{DataDir}/content/` sharded by first 4 hex chars:

```
data/content/ab/cd/abcdef1234567890...  (full SHA256 hex as filename)
```

The `DiskPath` in ContentObjects stores the relative path: `ab/cd/abcdef1234...`

Old per-bucket directories (`data/{bucketId}/`) remain during migration. New uploads go to content-addressed paths only.

## Upload Flow

1. Stream request body to temp file via `System.IO.Pipelines` (existing pattern), adding `IncrementalHash.CreateHash(SHA256)` in the fill pipe — hash every chunk as it's read from the network. Zero extra passes.
2. After write completes: have SHA256 hash + temp file on disk.
3. `BEGIN TRANSACTION`
4. Check `ContentObjects` for hash:
   - **EXISTS**: increment `RefCount`, delete temp file (dedup)
   - **NOT EXISTS**: move temp file to content-addressed path, insert `ContentObjects` row
5. Insert/update `Files` row with `ContentHash`
6. Update bucket stats
7. `COMMIT`
8. Return file metadata including `sha256` and `deduplicated: true/false`

`FileStorageService.StoreAsync` returns `(long size, string sha256Hash)`. Hashing integrated into `FillPipeFromStreamAsync`.

## Download Flow

1. Look up `Files` by `(BucketId, Path)` → get `ContentHash`
2. Look up `ContentObjects` by hash → get `DiskPath`
3. Serve `PhysicalFile()` from `{DataDir}/content/{DiskPath}`
4. **ETag = SHA256 hash** — content-based, proper HTTP semantics

## Delete Flow

1. Load file entity → get `ContentHash`
2. Delete `Files` row, update bucket stats (existing logic)
3. Decrement `RefCount` on `ContentObjects`
4. If `RefCount` reaches 0 — don't delete immediately. Background cleanup (added to existing `CleanupService`) purges orphans where `RefCount = 0 AND CreatedAt < (now - 1 hour)`.

## PATCH Rewrite

PATCH (partial file update) is rewritten for CAS compatibility:

1. Read original content from CAS path
2. Apply patch (offset/append) to a new temp file
3. Hash the result
4. Store as new content object (same flow as upload)
5. Update `Files.ContentHash` to new hash
6. Decrement old content's `RefCount`

## Unified /files Endpoint

Remove `/ls` endpoint. Enhance `GET /api/buckets/{id}/files`:

- **No `delimiter` param** → flat paginated list (current behavior, existing sort/limit/offset)
- **`delimiter=/` param** → tree mode. Optional `prefix` param for scoping.

Tree mode response:

```json
{
  "prefix": "src/",
  "delimiter": "/",
  "directories": [
    { "path": "src/utils/", "file_count": 5, "total_size": 12345 }
  ],
  "files": [...],
  "total_files": 1,
  "total_directories": 2,
  "cursor": null
}
```

Tree mode uses cursor-based pagination (last `path` from previous page). Flat mode keeps offset pagination for backwards compat.

SQL: range scan (`path >= @Prefix AND path < @PrefixEnd`) to get paths, partition into files vs directories in C# by checking for delimiter in remainder.

## Bucket Detail Changes

`GET /api/buckets/{id}` default response:
- No `files` array by default
- Add: `unique_content_count`, `unique_content_size`
- Add: `?include=files` query param to restore old behavior (first 100 files)

## Verification Endpoint

`GET /api/buckets/{id}/files/{*path}/verify` (authenticated, owner/admin):
- Read file from disk, recompute SHA256
- Compare to stored `ContentHash`
- Return `{ path, stored_hash, computed_hash, valid }`

## Migration (Migrator Project)

1. Add `ContentHash` column as nullable
2. Create `ContentObjects` table
3. For each file in `Files` where `ContentHash IS NULL`:
   - Read from old disk path
   - Compute SHA256
   - Insert `ContentObjects` (or increment `RefCount` if hash exists)
   - Move file to content-addressed path
   - Update `Files.ContentHash`
4. Clean up old bucket directories (optional separate step)

## Path Normalization

Add `PathNormalizer.Normalize()` applied on every upload:
- Backslash → forward slash
- Trim leading slash
- Collapse double slashes
- Reject `..` traversal
- Reject empty path components

## API Response Changes

`BucketFile` gains `sha256` field. `UploadResponse` items gain `deduplicated` field.

## Orphan Cleanup

Added as a new step in the existing `CleanupService` cycle:
- Query `ContentObjects WHERE RefCount = 0 AND CreatedAt < (now - 1 hour)`
- Delete disk files
- Delete DB rows
- The 1-hour grace period prevents deleting content referenced by concurrent uploads
