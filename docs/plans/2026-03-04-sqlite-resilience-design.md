# SQLite Resilience Hardening — Design

## Problem

We hit `SQLite Error 11: 'database disk image is malformed'` caused by a corrupted index (`IX_Files_ContentHash`). Root cause: container restart mid-write during CAS migration. `REINDEX` fixed it. This design adds defenses to detect, recover from, and prevent similar issues.

## Approach

**Option A (chosen):** Extend `DatabaseInitializer` for startup concerns + new `DatabaseHealthService` for runtime monitoring and graceful shutdown.

Alternatives considered:
- **Single service for everything** — mixes startup init with runtime monitoring, duplicates WAL pragma with existing initializer
- **Middleware approach** — overkill; SQLite WAL/synchronous PRAGMAs are database-level, not connection-level

## Changes

### 1. DatabaseInitializer — Startup PRAGMAs + Integrity Check

**File:** `src/CarbonFiles.Infrastructure/Data/DatabaseInitializer.cs`

Extend `Initialize()` to:

1. Set PRAGMAs before schema creation:
   - `PRAGMA journal_mode=WAL;` (already exists)
   - `PRAGMA synchronous=NORMAL;` (new — safe with WAL, faster than FULL)
   - `PRAGMA wal_autocheckpoint=1000;` (new — checkpoint every 1000 pages)

2. Run integrity check after schema creation:
   - `PRAGMA quick_check;`
   - If result != "ok": log warning, attempt `REINDEX;`
   - If REINDEX fails: log error, do **not** throw — let the app start

Add optional `ILogger?` parameter to `Initialize()` for logging integrity check results. Migrator and test callers pass null or a logger.

### 2. DatabaseHealthService — Background Monitoring + Graceful Shutdown

**New file:** `src/CarbonFiles.Infrastructure/Services/DatabaseHealthService.cs`

`DatabaseHealthService : BackgroundService` with its own dedicated singleton `SqliteConnection` (separate from DI-scoped request connections).

**ExecuteAsync:**
- Initial delay: 60 seconds
- Every 60 minutes:
  - `PRAGMA quick_check;`
  - If ok → log Info
  - If not ok → log Warning, run `REINDEX;`, re-run `quick_check`, log result
  - Catch exceptions → log Error, continue loop

**StopAsync (graceful shutdown):**
- `PRAGMA wal_checkpoint(TRUNCATE);` — flush WAL to main DB
- Log Info on success, log Error on failure (best-effort)
- Close and dispose the singleton connection

**Registration:** `services.AddHostedService<DatabaseHealthService>()` in `DependencyInjection.AddInfrastructure()`.

### 3. Transaction Audit

Existing multi-statement writes already use explicit `IDbTransaction`. Verify during implementation that every `Db.*` call within a transaction block actually passes the `tx` parameter. Fix any that don't.

Known transaction sites:
- `UploadService.StoreFileAsync()`
- `BucketService.DeleteAsync()`
- `CleanupRepository.DeleteBucketAndRelatedAsync()`
- `FileService` delete operations

### 4. No Changes Needed

- **Connection management** — scoped connections are fine; WAL mode handles concurrent access
- **Per-connection PRAGMAs** — `journal_mode` and `synchronous` are database-level in WAL mode, set once at startup

## Testing

- **Startup PRAGMAs:** Integration test that reads back `PRAGMA journal_mode`, `synchronous`, `wal_autocheckpoint` after initialization. Account for in-memory SQLite not supporting WAL.
- **Integrity check happy path:** Verified by existing tests passing (quick_check returns "ok").
- **Health service core logic:** Extract check logic into a testable method called by ExecuteAsync.
- **Graceful shutdown:** Manual verification via Docker logs. Not automated — single PRAGMA call.
- **Transaction audit:** Code review during implementation.

## Files Modified

| File | Change |
|------|--------|
| `src/CarbonFiles.Infrastructure/Data/DatabaseInitializer.cs` | Add PRAGMAs, integrity check, optional logger param |
| `src/CarbonFiles.Infrastructure/Services/DatabaseHealthService.cs` | **New** — background health check + graceful shutdown |
| `src/CarbonFiles.Infrastructure/DependencyInjection.cs` | Register DatabaseHealthService |
| `src/CarbonFiles.Api/Program.cs` | Pass logger to DatabaseInitializer.Initialize() |
| `tests/CarbonFiles.Api.Tests/` | PRAGMA verification test |
