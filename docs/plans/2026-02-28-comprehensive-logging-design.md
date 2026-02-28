# Comprehensive Logging Design

## Overview

Add detailed logging throughout CarbonFiles using built-in `Microsoft.Extensions.Logging` (`ILogger<T>`). No new NuGet packages. Every service, endpoint, and middleware gets structured logging at appropriate levels.

## Current State

Only 2 of ~18 files use `ILogger`: `CleanupService` (3 log calls) and `BucketEndpoints` (1 warning). No middleware logging, no auth logging, no request-level logging.

## Design

### Provider

Built-in Microsoft.Extensions.Logging with console/debug providers. Structured message templates for all log calls (e.g., `"Created bucket {BucketId} with expiry {Expiry}"`).

### Log Levels

| Level | Usage |
|-------|-------|
| Debug | Method entry, parameter details, internal decisions |
| Information | Successful operations, state changes |
| Warning | Degraded conditions, slow ops, auth failures, business rule violations |
| Error | Exceptions, failed operations |

### Components

#### 1. RequestLoggingMiddleware (New)

New file: `Api/Middleware/RequestLoggingMiddleware.cs`

Registered early in the pipeline. Wraps every request with timing and logs:
- Information: `HTTP {Method} {Path} -> {StatusCode} in {ElapsedMs}ms`
- Warning: Slow requests (>5s)
- Debug: Query parameters, content type

#### 2. AuthMiddleware Enhancement

Add `ILogger<AuthMiddleware>` to existing middleware:
- Information: Successful auth with token type
- Warning: Failed/missing auth attempts
- Debug: Token resolution flow

#### 3. Services (inject ILogger<T> into all 9)

| Service | Log Points |
|---------|-----------|
| BucketService | Create/update/delete with IDs, expiry, not-found cases |
| FileService | File listing, metadata access, deletions, not-found |
| UploadService | Upload start/completion with size, bucket stats updates |
| FileStorageService | Disk write/read/delete with paths and sizes |
| ApiKeyService | Key creation, deletion, validation |
| UploadTokenService | Token creation, validation, rate limit checks |
| DashboardTokenService | Token generation |
| ShortUrlService | Short URL creation, resolution, not-found |
| CleanupService | Already has logging - keep as-is |

#### 4. Endpoints

Add contextual logging in handlers:
- Information: Operation summaries ("Created bucket {BucketId}")
- Warning: Business rule violations (rate limits, size limits)
- Error: Exception context

#### 5. SignalR Hub

Log connect/disconnect and group membership changes.

### Configuration

**appsettings.json** (production):
```json
"Logging": {
  "LogLevel": {
    "Default": "Information",
    "Microsoft.AspNetCore": "Warning",
    "CarbonFiles": "Information"
  }
}
```

**appsettings.Development.json**:
```json
"Logging": {
  "LogLevel": {
    "Default": "Information",
    "Microsoft.AspNetCore": "Information",
    "CarbonFiles": "Debug"
  }
}
```

### Files Changed

- **New**: `src/CarbonFiles.Api/Middleware/RequestLoggingMiddleware.cs`
- **Modified**: `src/CarbonFiles.Api/Auth/AuthMiddleware.cs`
- **Modified**: All 8 services in `src/CarbonFiles.Infrastructure/Services/`
- **Modified**: All endpoint files in `src/CarbonFiles.Api/Endpoints/`
- **Modified**: `src/CarbonFiles.Api/Hubs/FileHub.cs`
- **Modified**: `src/CarbonFiles.Api/Program.cs`
- **Modified**: `appsettings.json`, `appsettings.Development.json`
