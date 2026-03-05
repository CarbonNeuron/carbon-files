# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

CarbonFiles is a file-sharing API with bucket-based organization, API key authentication, and real-time SignalR events. Built with ASP.NET Minimal API on .NET 10, published as a Native AOT binary. Uses Dapper with Dapper.AOT for source-generated, AOT-safe database access.

## Build & Development Commands

```bash
# Build
dotnet build

# Run locally (serves on http://localhost:5000 by default)
dotnet run --project src/CarbonFiles.Api

# Run all tests
dotnet test

# Run a specific test project
dotnet test tests/CarbonFiles.Api.Tests

# Run a single test by name
dotnet test --filter "FullyQualifiedName~BucketEndpointTests.CreateBucket"
```

### Schema Changes

After changing entity models, update the raw DDL in `DatabaseInitializer.Schema` (`src/CarbonFiles.Infrastructure/Data/DatabaseInitializer.cs`). This const is used by the API at startup, the Migrator, and test fixtures. All tables use `CREATE TABLE IF NOT EXISTS` for idempotency.

### Docker

```bash
docker compose up -d          # Run on port 8080
# Admin key: CarbonFiles__AdminKey env var
```

## Architecture

```
CarbonFiles.Api          → Endpoints, auth middleware, SignalR hub, JSON serialization
CarbonFiles.Core         → Domain models, interfaces, configuration, utilities
CarbonFiles.Infrastructure → Services, Dapper data access, auth implementation
CarbonFiles.Migrator     → Standalone schema initializer (used in Docker entrypoint)
```

### Key Patterns

- **Minimal API endpoints**: No controllers. Each feature has a static `Map*Endpoints()` extension method in `src/CarbonFiles.Api/Endpoints/` registered in `Program.cs`.
- **Service layer, no repository pattern**: Services in `Infrastructure/Services/` use `IDbConnection` (Dapper) directly. All registered via `DependencyInjection.AddInfrastructure()`.
- **Source-generated JSON**: `CarbonFilesJsonContext` in `Api/Serialization/` uses `[JsonSerializable]` attributes for AOT-compatible serialization. New request/response types must be added to this context.
- **Dapper.AOT**: Source-generated query execution via `[module: DapperAot]` in `Infrastructure/DapperConfig.cs`. Entity classes in `Data/Entities/` are plain POCOs that Dapper maps directly.
- **Database initialization**: Uses raw SQL DDL (`CREATE TABLE IF NOT EXISTS`) in `DatabaseInitializer.Schema`. Shared by the API at startup, the Migrator, and test fixtures.
- **Filesystem blob storage**: Files stored at `./data/{bucketId}/{url-encoded-path}`. Managed by singleton `FileStorageService`.

### Authentication

`AuthMiddleware` extracts a Bearer token and resolves it via `IAuthService` into an `AuthContext` stored in `HttpContext.Items`. Four token types:

- **Admin key** — env var `CarbonFiles__AdminKey`, full access
- **API keys** — `cf4_` prefix, SHA-256 hashed, scoped to own buckets, 30s cache
- **Dashboard JWT** — HMAC-SHA256, 24h max, admin-level access
- **Upload tokens** — `cfu_` prefix, scoped to a single bucket with optional rate limit

### Real-Time (SignalR)

Hub at `/hub/files`. Group-based: `bucket:{id}`, `file:{id}:{path}`, `global`. JSON protocol only (AOT constraint). `HubNotificationService` implements `INotificationService` to push events (FileCreated, FileUpdated, FileDeleted, BucketCreated, BucketUpdated, BucketDeleted).

## Testing

- **Framework**: xUnit + FluentAssertions
- **Integration tests** in `tests/CarbonFiles.Api.Tests/` use `WebApplicationFactory<Program>` with in-memory SQLite and a temp directory for file storage (see `TestFixture.cs`)
- **Test naming**: `MethodName_Scenario_ExpectedResult`
- **CancellationToken**: Pass `TestContext.Current.CancellationToken` in all async test calls
- **Fixture**: `TestFixture` provides `CreateAdminClient()`, `CreateApiKeyClientAsync()`, `CreateAuthenticatedClient(token)`, and `GetServerUrl()` for SignalR tests

## Conventions

- Snake_case JSON naming (`PropertyNamingPolicy.SnakeCaseLower`), nulls omitted
- API error responses: `{"error": "...", "hint": "..."}`
- ID generation: crypto-random via `IdGenerator` — 10-char bucket IDs, 6-char short codes, `cf4_`/`cfu_` prefixed keys
- Expiry parsing: `ExpiryParser` handles duration strings (1h, 1d, 1w, 30d), Unix timestamps, and ISO 8601
- SQLite with WAL mode for concurrent access
- Pagination params: `limit`, `offset`, `sort`, `order`

## Client SDKs

Four client SDKs under `clients/`:

| Language | Package | Generator | Dir |
|---|---|---|---|
| TypeScript | `@carbonfiles/client` (npm) | Hey API (`@hey-api/openapi-ts`) | `clients/typescript/` |
| C# | `CarbonFiles.Client` (NuGet) | Hand-crafted | `clients/csharp/` |
| Python | `carbonfiles` (PyPI) | Hand-crafted | `clients/python/` |
| PowerShell | `CarbonFiles` (PSGallery) | Hand-crafted | `clients/powershell/` |

### Regenerating Clients Locally

```bash
./scripts/export-openapi.sh openapi.json
# TypeScript
cp openapi.json clients/typescript/ && cd clients/typescript && npm run generate && npm run build
# C# (hand-crafted, no generation needed — just build)
dotnet build clients/csharp/ -c Release
# Python (hand-crafted, no generation needed — just build)
cd clients/python && pip install -e ".[dev]" && python -m pytest
```

Publishing is automated via `.github/workflows/publish-clients.yml` on GitHub Release.
