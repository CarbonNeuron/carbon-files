# SQLite Resilience Hardening Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Add startup PRAGMAs, integrity checks, background health monitoring, and graceful WAL shutdown to prevent and recover from SQLite corruption.

**Architecture:** Extend `DatabaseInitializer` for startup concerns (PRAGMAs + quick_check + REINDEX). Add new `DatabaseHealthService : BackgroundService` with a dedicated singleton `SqliteConnection` for hourly health checks and graceful WAL checkpoint on shutdown.

**Tech Stack:** ASP.NET Minimal API, Microsoft.Data.Sqlite, BackgroundService

---

### Task 1: Extend DatabaseInitializer with PRAGMAs and Integrity Check

**Files:**
- Modify: `src/CarbonFiles.Infrastructure/Data/DatabaseInitializer.cs`

**Step 1: Add the new PRAGMAs and integrity check logic**

Replace the `Initialize` method to add `synchronous=NORMAL`, `wal_autocheckpoint=1000`, and a `quick_check` → `REINDEX` fallback. Add an optional `ILogger?` parameter for logging integrity results.

```csharp
using System.Data;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace CarbonFiles.Infrastructure.Data;

/// <summary>
/// Initializes the SQLite database schema using raw SQL.
/// Both Migrate() and EnsureCreated() rely on design-time operations
/// that are trimmed under Native AOT. This class creates tables directly.
/// </summary>
public static class DatabaseInitializer
{
    internal const string Schema = """
        CREATE TABLE IF NOT EXISTS "Buckets" (
            "Id" TEXT NOT NULL CONSTRAINT "PK_Buckets" PRIMARY KEY,
            "Name" TEXT NOT NULL,
            "Owner" TEXT NOT NULL,
            "OwnerKeyPrefix" TEXT NULL,
            "Description" TEXT NULL,
            "CreatedAt" TEXT NOT NULL,
            "ExpiresAt" TEXT NULL,
            "LastUsedAt" TEXT NULL,
            "FileCount" INTEGER NOT NULL DEFAULT 0,
            "TotalSize" INTEGER NOT NULL DEFAULT 0,
            "DownloadCount" INTEGER NOT NULL DEFAULT 0
        );

        CREATE INDEX IF NOT EXISTS "IX_Buckets_OwnerKeyPrefix" ON "Buckets" ("OwnerKeyPrefix");
        CREATE INDEX IF NOT EXISTS "IX_Buckets_ExpiresAt" ON "Buckets" ("ExpiresAt");
        CREATE INDEX IF NOT EXISTS "IX_Buckets_Owner" ON "Buckets" ("Owner");

        CREATE TABLE IF NOT EXISTS "ContentObjects" (
            "Hash" TEXT NOT NULL CONSTRAINT "PK_ContentObjects" PRIMARY KEY,
            "Size" INTEGER NOT NULL,
            "DiskPath" TEXT NOT NULL,
            "RefCount" INTEGER NOT NULL DEFAULT 1,
            "CreatedAt" TEXT NOT NULL
        );

        CREATE INDEX IF NOT EXISTS "IX_ContentObjects_Orphans"
            ON "ContentObjects" ("RefCount") WHERE "RefCount" = 0;

        CREATE TABLE IF NOT EXISTS "Files" (
            "BucketId" TEXT NOT NULL,
            "Path" TEXT NOT NULL,
            "Name" TEXT NOT NULL,
            "Size" INTEGER NOT NULL DEFAULT 0,
            "MimeType" TEXT NOT NULL,
            "ShortCode" TEXT NULL,
            "ContentHash" TEXT NULL,
            "CreatedAt" TEXT NOT NULL,
            "UpdatedAt" TEXT NOT NULL,
            CONSTRAINT "PK_Files" PRIMARY KEY ("BucketId", "Path")
        );

        CREATE INDEX IF NOT EXISTS "IX_Files_BucketId" ON "Files" ("BucketId");
        CREATE UNIQUE INDEX IF NOT EXISTS "IX_Files_ShortCode" ON "Files" ("ShortCode") WHERE "ShortCode" IS NOT NULL;
        CREATE INDEX IF NOT EXISTS "IX_Files_ContentHash" ON "Files" ("ContentHash");

        CREATE TABLE IF NOT EXISTS "ApiKeys" (
            "Prefix" TEXT NOT NULL CONSTRAINT "PK_ApiKeys" PRIMARY KEY,
            "HashedSecret" TEXT NOT NULL,
            "Name" TEXT NOT NULL,
            "CreatedAt" TEXT NOT NULL,
            "LastUsedAt" TEXT NULL
        );

        CREATE TABLE IF NOT EXISTS "ShortUrls" (
            "Code" TEXT NOT NULL CONSTRAINT "PK_ShortUrls" PRIMARY KEY,
            "BucketId" TEXT NOT NULL,
            "FilePath" TEXT NOT NULL,
            "CreatedAt" TEXT NOT NULL
        );

        CREATE INDEX IF NOT EXISTS "IX_ShortUrls_BucketId_FilePath" ON "ShortUrls" ("BucketId", "FilePath");

        CREATE TABLE IF NOT EXISTS "UploadTokens" (
            "Token" TEXT NOT NULL CONSTRAINT "PK_UploadTokens" PRIMARY KEY,
            "BucketId" TEXT NOT NULL,
            "ExpiresAt" TEXT NOT NULL,
            "MaxUploads" INTEGER NULL,
            "UploadsUsed" INTEGER NOT NULL DEFAULT 0,
            "CreatedAt" TEXT NOT NULL
        );

        CREATE INDEX IF NOT EXISTS "IX_UploadTokens_BucketId" ON "UploadTokens" ("BucketId");
        """;

    public static void Initialize(IDbConnection db, ILogger? logger = null)
    {
        var sqlite = (SqliteConnection)db;

        // WAL mode + resilience PRAGMAs
        using var pragmaCmd = sqlite.CreateCommand();
        pragmaCmd.CommandText = """
            PRAGMA journal_mode=WAL;
            PRAGMA synchronous=NORMAL;
            PRAGMA wal_autocheckpoint=1000;
            """;
        pragmaCmd.ExecuteNonQuery();

        // Schema
        using var schemaCmd = sqlite.CreateCommand();
        schemaCmd.CommandText = Schema;
        schemaCmd.ExecuteNonQuery();

        // Integrity check
        RunIntegrityCheck(sqlite, logger);
    }

    internal static bool RunIntegrityCheck(SqliteConnection sqlite, ILogger? logger)
    {
        using var checkCmd = sqlite.CreateCommand();
        checkCmd.CommandText = "PRAGMA quick_check;";
        var result = checkCmd.ExecuteScalar()?.ToString();

        if (result == "ok")
        {
            logger?.LogInformation("Database integrity check passed");
            return true;
        }

        logger?.LogWarning("Database integrity check failed: {Result}. Attempting REINDEX", result);
        try
        {
            using var reindexCmd = sqlite.CreateCommand();
            reindexCmd.CommandText = "REINDEX;";
            reindexCmd.ExecuteNonQuery();
            logger?.LogInformation("REINDEX completed successfully");
            return true;
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "REINDEX failed — database may have corruption. Manual intervention may be required");
            return false;
        }
    }
}
```

**Step 2: Run the build**

Run: `dotnet build src/CarbonFiles.Infrastructure/`
Expected: Build succeeded

**Step 3: Update callers — Program.cs**

In `src/CarbonFiles.Api/Program.cs`, change lines 173-177 to pass a logger:

```csharp
// Initialize database schema + WAL mode + integrity check
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<IDbConnection>();
    var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("DatabaseInitializer");
    DatabaseInitializer.Initialize(db, logger);
}
```

**Step 4: Run the build**

Run: `dotnet build src/CarbonFiles.Api/`
Expected: Build succeeded

**Step 5: Run existing tests to confirm no regressions**

Run: `dotnet test tests/CarbonFiles.Api.Tests/`
Expected: All tests pass. `TestFixture` and `Migrator` call `Initialize()` without the logger param — the default `null` handles this.

**Step 6: Commit**

```bash
git add src/CarbonFiles.Infrastructure/Data/DatabaseInitializer.cs src/CarbonFiles.Api/Program.cs
git commit -m "feat: add startup PRAGMAs and integrity check to DatabaseInitializer"
```

---

### Task 2: Create DatabaseHealthService

**Files:**
- Create: `src/CarbonFiles.Infrastructure/Services/DatabaseHealthService.cs`
- Modify: `src/CarbonFiles.Infrastructure/DependencyInjection.cs:68-72`

**Step 1: Create the health service**

Create `src/CarbonFiles.Infrastructure/Services/DatabaseHealthService.cs`:

```csharp
using CarbonFiles.Core.Configuration;
using CarbonFiles.Infrastructure.Data;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CarbonFiles.Infrastructure.Services;

public sealed class DatabaseHealthService : BackgroundService, IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly ILogger<DatabaseHealthService> _logger;

    public DatabaseHealthService(IOptions<CarbonFilesOptions> options, ILogger<DatabaseHealthService> logger)
    {
        _logger = logger;
        var connectionString = $"Data Source={options.Value.DbPath}";
        _connection = new SqliteConnection(connectionString);
        _connection.Open();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Wait for app to fully start before first check
        await Task.Delay(TimeSpan.FromSeconds(60), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                RunHealthCheck();
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during database health check");
            }

            await Task.Delay(TimeSpan.FromMinutes(60), stoppingToken);
        }
    }

    internal void RunHealthCheck()
    {
        var ok = DatabaseInitializer.RunIntegrityCheck(_connection, _logger);
        if (!ok)
        {
            // Re-check after REINDEX attempt (REINDEX already ran inside RunIntegrityCheck)
            using var recheckCmd = _connection.CreateCommand();
            recheckCmd.CommandText = "PRAGMA quick_check;";
            var result = recheckCmd.ExecuteScalar()?.ToString();
            if (result == "ok")
                _logger.LogInformation("Database integrity restored after REINDEX");
            else
                _logger.LogError("Database integrity still failing after REINDEX: {Result}", result);
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken);

        try
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = "PRAGMA wal_checkpoint(TRUNCATE);";
            cmd.ExecuteNonQuery();
            _logger.LogInformation("WAL checkpoint completed on shutdown");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to checkpoint WAL on shutdown");
        }
        finally
        {
            await _connection.DisposeAsync();
        }
    }

    public override void Dispose()
    {
        _connection.Dispose();
        base.Dispose();
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        await _connection.DisposeAsync();
        base.Dispose();
    }
}
```

**Step 2: Register the service in DI**

In `src/CarbonFiles.Infrastructure/DependencyInjection.cs`, add after line 70 (`services.AddHostedService<CleanupService>()`):

```csharp
        services.AddHostedService<DatabaseHealthService>();
```

**Step 3: Build**

Run: `dotnet build src/CarbonFiles.Infrastructure/`
Expected: Build succeeded

**Step 4: Run existing tests**

Run: `dotnet test tests/CarbonFiles.Api.Tests/`
Expected: All pass. The health service will start in the test host but the 60s initial delay means it won't run during short test execution. If the in-memory DB connection string causes issues, we can exclude it in test config — but it should be fine since `IOptions<CarbonFilesOptions>` is configured with a test DbPath.

**Step 5: Commit**

```bash
git add src/CarbonFiles.Infrastructure/Services/DatabaseHealthService.cs src/CarbonFiles.Infrastructure/DependencyInjection.cs
git commit -m "feat: add DatabaseHealthService for background integrity checks and graceful WAL shutdown"
```

---

### Task 3: Transaction Audit

**Files:**
- Read-only audit of:
  - `src/CarbonFiles.Infrastructure/Services/UploadService.cs`
  - `src/CarbonFiles.Infrastructure/Services/BucketService.cs`
  - `src/CarbonFiles.Infrastructure/Services/FileService.cs`
  - `src/CarbonFiles.Infrastructure/Services/CleanupRepository.cs`

**Step 1: Verify every `Db.*` call within a `BeginTransaction()` block passes `tx`**

Check each transaction site. From the codebase grep, all calls pass `tx`:

- **UploadService.cs**: lines 83-140 and 157-217 — every `Db.ExecuteAsync` passes `tx` ✓
- **BucketService.cs**: lines 301-312 — every `Db.ExecuteAsync` passes `tx` ✓
- **CleanupRepository.cs**: lines 43-63 — every `Db.ExecuteAsync` passes `tx` and `ct` ✓
- **FileService.cs**: lines 314-346 and 401-461 — every `Db.ExecuteAsync` passes `tx` ✓

**Step 2: Confirm — no code changes needed**

All transaction sites correctly pass the `IDbTransaction` to every database call within the transaction scope. No fixes required.

**Step 3: No commit needed** — audit only.

---

### Task 4: Add Integration Test for Startup PRAGMAs

**Files:**
- Create: `tests/CarbonFiles.Api.Tests/DatabaseInitializerTests.cs`

**Step 1: Write the test**

Create `tests/CarbonFiles.Api.Tests/DatabaseInitializerTests.cs`:

```csharp
using CarbonFiles.Infrastructure.Data;
using Microsoft.Data.Sqlite;
using Xunit;

namespace CarbonFiles.Api.Tests;

public class DatabaseInitializerTests : IDisposable
{
    private readonly SqliteConnection _connection;

    public DatabaseInitializerTests()
    {
        // Use a temp file DB — in-memory SQLite doesn't support WAL
        var dbPath = Path.Combine(Path.GetTempPath(), $"cf_pragma_test_{Guid.NewGuid():N}.db");
        _connection = new SqliteConnection($"Data Source={dbPath}");
        _connection.Open();
        DatabaseInitializer.Initialize(_connection);
    }

    [Fact]
    public void Initialize_SetsWalJournalMode()
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "PRAGMA journal_mode;";
        var result = cmd.ExecuteScalar()?.ToString();
        Assert.Equal("wal", result);
    }

    [Fact]
    public void Initialize_SetsSynchronousNormal()
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "PRAGMA synchronous;";
        var result = Convert.ToInt32(cmd.ExecuteScalar());
        // synchronous=NORMAL is 1
        Assert.Equal(1, result);
    }

    [Fact]
    public void Initialize_SetsWalAutocheckpoint()
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "PRAGMA wal_autocheckpoint;";
        var result = Convert.ToInt32(cmd.ExecuteScalar());
        Assert.Equal(1000, result);
    }

    [Fact]
    public void Initialize_IntegrityCheckPasses()
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "PRAGMA quick_check;";
        var result = cmd.ExecuteScalar()?.ToString();
        Assert.Equal("ok", result);
    }

    public void Dispose()
    {
        var dbPath = _connection.DataSource;
        _connection.Dispose();
        // Clean up temp DB files
        try { File.Delete(dbPath); } catch { }
        try { File.Delete(dbPath + "-wal"); } catch { }
        try { File.Delete(dbPath + "-shm"); } catch { }
    }
}
```

**Step 2: Run the new tests**

Run: `dotnet test tests/CarbonFiles.Api.Tests/ --filter "FullyQualifiedName~DatabaseInitializerTests"`
Expected: All 4 tests pass

**Step 3: Run the full test suite**

Run: `dotnet test tests/CarbonFiles.Api.Tests/`
Expected: All tests pass (existing + new)

**Step 4: Commit**

```bash
git add tests/CarbonFiles.Api.Tests/DatabaseInitializerTests.cs
git commit -m "test: add PRAGMA verification tests for DatabaseInitializer"
```

---

### Task 5: Final Verification

**Step 1: Full build**

Run: `dotnet build`
Expected: Build succeeded, 0 warnings related to our changes

**Step 2: Full test suite**

Run: `dotnet test`
Expected: All tests pass

**Step 3: Manual smoke test (optional)**

Run: `dotnet run --project src/CarbonFiles.Api`
Expected: App starts, logs show:
- "Database integrity check passed" at startup
- No errors

Stop with Ctrl+C, logs should show:
- "WAL checkpoint completed on shutdown"

**Step 4: Final commit if any cleanup needed, then done.**
