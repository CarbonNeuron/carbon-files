# CarbonFiles API — Implementation Design

**Date:** 2026-02-27
**Spec:** [docs/carbon-files-api.md](../carbon-files-api.md)
**Source:** https://clawd-files.plexus.video/raw/5ZC4htoA-T/carbon-files-api.md

## Summary

CarbonFiles is a file-sharing API with bucket-based organization, API key authentication, and real-time events. ASP.NET Minimal API on .NET 10, Native AOT compiled, EF Core + SQLite for metadata, filesystem for blob storage.

## Architecture Decisions

### Service Layer: Direct Services

Simple service interfaces in Core, implementations in Infrastructure. No mediator pattern, no CQRS, no repository abstraction layer.

```
Endpoint → IService → DbContext + FileSystem
```

Services:
- `IBucketService` — CRUD, expiry, ZIP streaming, summary generation
- `IFileService` — metadata, content streaming, range requests, partial updates
- `IApiKeyService` — key lifecycle, usage stats
- `IUploadService` — multipart + stream uploads, upload token management
- `IShortUrlService` — code generation, resolution, cleanup
- `IDashboardTokenService` — JWT creation + validation
- `IAuthService` — token resolution (admin key, API key, JWT, upload token)
- `ICleanupService` — background expired bucket cleanup

### Endpoint Organization

Static extension methods on `IEndpointRouteBuilder`, one file per feature group:

```
CarbonFiles.Api/
├── Endpoints/
│   ├── HealthEndpoints.cs
│   ├── KeyEndpoints.cs
│   ├── TokenEndpoints.cs
│   ├── StatsEndpoints.cs
│   ├── BucketEndpoints.cs
│   ├── FileEndpoints.cs
│   ├── UploadEndpoints.cs
│   └── ShortUrlEndpoints.cs
├── Auth/
│   ├── AuthMiddleware.cs
│   ├── AuthContext.cs
│   └── AuthExtensions.cs
├── Hubs/
│   └── FileHub.cs
└── Program.cs
```

### Authentication Flow

Custom middleware sets `AuthContext` on `HttpContext.Items`:

1. Extract `Authorization: Bearer <token>` header
2. If token matches admin key → `AuthContext.Admin`
3. If token starts with `cf4_` → look up API key in DB (cached in `MemoryCache`, 30s TTL) → `AuthContext.Owner(keyName)`
4. If token is a valid JWT with `scope: admin` → `AuthContext.Admin`
5. No token → `AuthContext.Public`
6. Upload token checked separately at upload endpoints via `?token=` query param

Each endpoint checks `AuthContext` and returns 401/403 as needed. No ASP.NET authorization policies — explicit checks in endpoint handlers for clarity.

### JSON Serialization (AOT)

Single `CarbonFilesJsonContext` registered as the default serializer:

```csharp
[JsonSerializable(typeof(BucketResponse))]
[JsonSerializable(typeof(FileInfoResponse))]
[JsonSerializable(typeof(ApiKeyResponse))]
// ... all request/response DTOs
// ... SignalR payload types (BucketChanges, etc.)
[JsonSerializable(typeof(ErrorResponse))]
[JsonSerializable(typeof(PaginatedResponse<BucketResponse>))]
internal partial class CarbonFilesJsonContext : JsonSerializerContext { }
```

### EF Core Entities vs API DTOs

Separate EF entities from API response DTOs:
- **Entities** (Infrastructure): `BucketEntity`, `FileEntity`, `ApiKeyEntity`, `ShortUrlEntity`, `UploadTokenEntity` — map to SQLite tables, include hashed key secret, internal fields
- **DTOs** (Core): `BucketResponse`, `FileInfoResponse`, etc. — match the API spec exactly
- **Mapping**: Extension methods on entities → DTOs, no AutoMapper

### File Storage

```
{DataDir}/
├── {bucketId}/
│   ├── readme.md
│   ├── src%2Fmain.rs          # URL-encoded path separators
│   └── .tmp/                   # Temp files for atomic writes
```

Path encoding: replace `/` with `%2F`, lowercase everything on disk. Decode on read.

### SignalR Integration

`IHubContext<FileHub>` injected into services. Services call hub notifications after state changes:

```csharp
// In UploadService after file is saved:
await _hubContext.Clients.Group($"bucket:{bucketId}").SendAsync("FileCreated", bucketId, fileInfo);
await _hubContext.Clients.Group($"file:{bucketId}:{path}").SendAsync("FileCreated", bucketId, fileInfo);
```

### Background Cleanup

`IHostedService` that runs on a timer (`CleanupIntervalMinutes`). Queries for expired buckets, deletes files from disk, removes DB records. Logs warnings for large deletions.

### Testing Strategy

- **Core.Tests**: Unit tests for expiry parsing, MIME detection, ID generation, auth context resolution
- **Infrastructure.Tests**: Unit tests for services using in-memory SQLite, file system operations using temp directories
- **Api.Tests**: Integration tests via `WebApplicationFactory<Program>`. Custom test fixture that sets up test admin key, temp data directory, in-memory SQLite. Tests every endpoint with valid/invalid auth, pagination, streaming, range requests, SignalR events.

## Not In Scope

- Rate limiting (mentioned as optional in spec)
- External message broker
- Frontend/dashboard UI
- Multi-instance deployment (single-process SignalR is fine)
