# Comprehensive Logging Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Add detailed structured logging throughout CarbonFiles using built-in Microsoft.Extensions.Logging (`ILogger<T>`) — no new packages.

**Architecture:** Inject `ILogger<T>` into every service, add logging to AuthMiddleware, create a new RequestLoggingMiddleware for HTTP request/response timing, and add contextual log statements in endpoints and the SignalR hub.

**Tech Stack:** Microsoft.Extensions.Logging, ILogger<T>, structured message templates

---

### Task 1: Update appsettings configuration

**Files:**
- Modify: `src/CarbonFiles.Api/appsettings.json`
- Modify: `src/CarbonFiles.Api/appsettings.Development.json`

**Step 1: Update appsettings.json**

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning",
      "CarbonFiles": "Information"
    }
  },
  "CarbonFiles": {
    "AdminKey": "change-me-in-production",
    "DataDir": "./data",
    "DbPath": "./data/carbonfiles.db",
    "MaxUploadSize": 0,
    "CleanupIntervalMinutes": 60,
    "CorsOrigins": "*"
  }
}
```

**Step 2: Update appsettings.Development.json**

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Information",
      "CarbonFiles": "Debug"
    }
  },
  "CarbonFiles": {
    "AdminKey": "dev-admin-key-change-me"
  }
}
```

**Step 3: Build to verify**

Run: `dotnet build`
Expected: Build succeeded

**Step 4: Commit**

```bash
git add src/CarbonFiles.Api/appsettings.json src/CarbonFiles.Api/appsettings.Development.json
git commit -m "feat: add CarbonFiles namespace to logging configuration"
```

---

### Task 2: Create RequestLoggingMiddleware

**Files:**
- Create: `src/CarbonFiles.Api/Middleware/RequestLoggingMiddleware.cs`
- Modify: `src/CarbonFiles.Api/Program.cs`

**Step 1: Create the middleware**

Create `src/CarbonFiles.Api/Middleware/RequestLoggingMiddleware.cs`:

```csharp
using System.Diagnostics;
using CarbonFiles.Api.Auth;

namespace CarbonFiles.Api.Middleware;

public sealed class RequestLoggingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<RequestLoggingMiddleware> _logger;

    public RequestLoggingMiddleware(RequestDelegate next, ILogger<RequestLoggingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var stopwatch = Stopwatch.StartNew();
        var method = context.Request.Method;
        var path = context.Request.Path;

        _logger.LogDebug("HTTP {Method} {Path} started", method, path);

        try
        {
            await _next(context);
        }
        finally
        {
            stopwatch.Stop();
            var statusCode = context.Response.StatusCode;
            var elapsed = stopwatch.ElapsedMilliseconds;

            var auth = context.Items["AuthContext"] as CarbonFiles.Core.Models.AuthContext;
            var tokenType = auth?.TokenType ?? "none";

            if (elapsed > 5000)
            {
                _logger.LogWarning("HTTP {Method} {Path} -> {StatusCode} in {ElapsedMs}ms [Auth: {TokenType}] (slow)",
                    method, path, statusCode, elapsed, tokenType);
            }
            else
            {
                _logger.LogInformation("HTTP {Method} {Path} -> {StatusCode} in {ElapsedMs}ms [Auth: {TokenType}]",
                    method, path, statusCode, elapsed, tokenType);
            }
        }
    }
}
```

**Step 2: Register middleware in Program.cs**

In `Program.cs`, add the `RequestLoggingMiddleware` BEFORE `UseCors()` and `AuthMiddleware`. Add this using statement at the top:

```csharp
using CarbonFiles.Api.Middleware;
```

Then after `app.UseForwardedHeaders();` and before `app.UseCors();`, add:

```csharp
app.UseMiddleware<RequestLoggingMiddleware>();
```

The middleware section should look like:

```csharp
// Middleware
app.UseMiddleware<RequestLoggingMiddleware>();
app.UseCors();
app.UseMiddleware<AuthMiddleware>();
```

**Step 3: Check that AuthContext has a TokenType property**

Look at `AuthContext` in `CarbonFiles.Core/Models/AuthContext.cs`. If it doesn't have a `TokenType` property, the middleware should use a fallback:
- If `auth.IsAdmin` → "admin"
- If `auth.IsOwner` → "api_key"
- If `auth.IsPublic` → "public"
- Otherwise → "unknown"

Adjust the middleware to use this pattern instead of `auth?.TokenType`:

```csharp
var tokenType = auth switch
{
    { IsAdmin: true } => "admin",
    { IsOwner: true } => "api_key",
    _ => "public"
};
```

**Step 4: Build and run tests**

Run: `dotnet build`
Expected: Build succeeded

Run: `dotnet test`
Expected: All tests pass

**Step 5: Commit**

```bash
git add src/CarbonFiles.Api/Middleware/RequestLoggingMiddleware.cs src/CarbonFiles.Api/Program.cs
git commit -m "feat: add request logging middleware with timing and auth context"
```

---

### Task 3: Add logging to AuthMiddleware

**Files:**
- Modify: `src/CarbonFiles.Api/Auth/AuthMiddleware.cs`

**Step 1: Add ILogger to AuthMiddleware**

Replace the full file with:

```csharp
using CarbonFiles.Core.Interfaces;

namespace CarbonFiles.Api.Auth;

public sealed class AuthMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<AuthMiddleware> _logger;

    public AuthMiddleware(RequestDelegate next, ILogger<AuthMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context, IAuthService authService)
    {
        var authHeader = context.Request.Headers.Authorization.FirstOrDefault();
        var token = authHeader?.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) == true
            ? authHeader["Bearer ".Length..]
            : null;

        _logger.LogDebug("Resolving auth for {Method} {Path}", context.Request.Method, context.Request.Path);

        var authContext = await authService.ResolveAsync(token);
        context.Items["AuthContext"] = authContext;

        if (token != null && authContext.IsPublic)
        {
            _logger.LogWarning("Auth failed for {Method} {Path} — invalid token provided", context.Request.Method, context.Request.Path);
        }
        else if (!authContext.IsPublic)
        {
            var tokenType = authContext.IsAdmin ? "admin" : "api_key";
            _logger.LogDebug("Auth resolved as {TokenType} for {Method} {Path}", tokenType, context.Request.Method, context.Request.Path);
        }

        await _next(context);
    }
}
```

**Step 2: Build and run tests**

Run: `dotnet build && dotnet test`
Expected: All pass

**Step 3: Commit**

```bash
git add src/CarbonFiles.Api/Auth/AuthMiddleware.cs
git commit -m "feat: add logging to AuthMiddleware for auth resolution and failures"
```

---

### Task 4: Add logging to AuthService

**Files:**
- Modify: `src/CarbonFiles.Infrastructure/Auth/AuthService.cs`

**Step 1: Add ILogger to AuthService**

Add `ILogger<AuthService>` to the constructor. Add these log statements:

- `LogDebug` at the start of `ResolveAsync` for the token type being resolved
- `LogInformation` when admin key is matched
- `LogDebug` when API key is resolved from cache
- `LogInformation` when API key is validated successfully
- `LogWarning` when API key validation fails (invalid secret)
- `LogInformation` when dashboard JWT is validated
- `LogDebug` when no token / public access

Full updated file:

```csharp
using CarbonFiles.Core.Interfaces;
using CarbonFiles.Core.Models;
using CarbonFiles.Core.Configuration;
using CarbonFiles.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CarbonFiles.Infrastructure.Auth;

public sealed class AuthService : IAuthService
{
    private readonly CarbonFilesDbContext _db;
    private readonly CarbonFilesOptions _options;
    private readonly JwtHelper _jwt;
    private readonly IMemoryCache _cache;
    private readonly ILogger<AuthService> _logger;
    private static readonly TimeSpan CacheDuration = TimeSpan.FromSeconds(30);

    public AuthService(CarbonFilesDbContext db, IOptions<CarbonFilesOptions> options, JwtHelper jwt, IMemoryCache cache, ILogger<AuthService> logger)
    {
        _db = db;
        _options = options.Value;
        _jwt = jwt;
        _cache = cache;
        _logger = logger;
    }

    public async Task<AuthContext> ResolveAsync(string? bearerToken)
    {
        if (string.IsNullOrEmpty(bearerToken))
        {
            _logger.LogDebug("No bearer token provided, resolving as public");
            return AuthContext.Public();
        }

        // 1. Check admin key
        if (bearerToken == _options.AdminKey)
        {
            _logger.LogInformation("Admin key authenticated");
            return AuthContext.Admin();
        }

        // 2. Check API key (cf4_ prefix)
        if (bearerToken.StartsWith("cf4_"))
        {
            var cacheKey = $"apikey:{bearerToken}";
            if (_cache.TryGetValue(cacheKey, out (string Name, string Prefix) cached))
            {
                _logger.LogDebug("API key {Prefix} resolved from cache", cached.Prefix);
                return AuthContext.Owner(cached.Name, cached.Prefix);
            }

            var result = await ValidateApiKeyAsync(bearerToken);
            if (result != null)
            {
                _cache.Set(cacheKey, result.Value, CacheDuration);
                _logger.LogInformation("API key {Prefix} authenticated for {Name}", result.Value.Prefix, result.Value.Name);
                return AuthContext.Owner(result.Value.Name, result.Value.Prefix);
            }
            _logger.LogWarning("Invalid API key attempted with prefix {Prefix}", bearerToken.Split('_', 3) is [_, var p, ..] ? $"cf4_{p}" : "unknown");
            return AuthContext.Public(); // Invalid API key
        }

        // 3. Check dashboard JWT
        var (isValid, _) = _jwt.ValidateToken(bearerToken);
        if (isValid)
        {
            _logger.LogInformation("Dashboard JWT authenticated");
            return AuthContext.Admin();
        }

        _logger.LogDebug("Bearer token did not match any auth method");
        return AuthContext.Public();
    }

    private async Task<(string Name, string Prefix)?> ValidateApiKeyAsync(string fullKey)
    {
        // cf4_{8hex}_{32hex}
        var parts = fullKey.Split('_', 3);
        if (parts.Length != 3 || parts[0] != "cf4") return null;

        var prefix = $"cf4_{parts[1]}";
        var secret = parts[2];

        var entity = await _db.ApiKeys.FirstOrDefaultAsync(k => k.Prefix == prefix);
        if (entity == null) return null;

        var hashed = Convert.ToHexStringLower(
            System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes(secret)));

        if (hashed != entity.HashedSecret) return null;

        // Update last_used_at
        entity.LastUsedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        return (entity.Name, entity.Prefix);
    }
}
```

**Step 2: Build and run tests**

Run: `dotnet build && dotnet test`
Expected: All pass

**Step 3: Commit**

```bash
git add src/CarbonFiles.Infrastructure/Auth/AuthService.cs
git commit -m "feat: add logging to AuthService for token resolution"
```

---

### Task 5: Add logging to BucketService

**Files:**
- Modify: `src/CarbonFiles.Infrastructure/Services/BucketService.cs`

**Step 1: Add ILogger to BucketService**

Add `using Microsoft.Extensions.Logging;` to usings. Add `ILogger<BucketService> _logger` field and inject via constructor:

```csharp
private readonly ILogger<BucketService> _logger;

public BucketService(CarbonFilesDbContext db, IOptions<CarbonFilesOptions> options, INotificationService notifications, ILogger<BucketService> logger)
{
    _db = db;
    _dataDir = options.Value.DataDir;
    _notifications = notifications;
    _logger = logger;
}
```

Add log statements:

In `CreateAsync` after `SaveChangesAsync`:
```csharp
_logger.LogInformation("Created bucket {BucketId} with name {Name} for owner {Owner}, expires {ExpiresAt}",
    bucketId, request.Name, owner, expiresAt?.ToString("o") ?? "never");
```

In `ListAsync` at the start:
```csharp
_logger.LogDebug("Listing buckets for {AuthType} (includeExpired={IncludeExpired}, limit={Limit}, offset={Offset})",
    auth.IsAdmin ? "admin" : auth.OwnerName ?? "public", includeExpired, pagination.Limit, pagination.Offset);
```

In `GetByIdAsync` when bucket not found or expired:
```csharp
if (entity == null)
{
    _logger.LogDebug("Bucket {BucketId} not found", id);
    return null;
}
if (entity.ExpiresAt.HasValue && entity.ExpiresAt.Value <= DateTime.UtcNow)
{
    _logger.LogDebug("Bucket {BucketId} is expired", id);
    return null;
}
```

In `UpdateAsync` after save:
```csharp
_logger.LogInformation("Updated bucket {BucketId}", id);
```

When update returns null (not found or no access):
```csharp
// Before the existing null return
if (entity == null)
{
    _logger.LogDebug("Bucket {BucketId} not found for update", id);
    return null;
}
// After the CanManage check
if (!auth.CanManage(entity.Owner))
{
    _logger.LogWarning("Access denied: update bucket {BucketId} by {Owner}", id, auth.OwnerName ?? "unknown");
    return null;
}
```

In `DeleteAsync` after successful delete:
```csharp
_logger.LogInformation("Deleted bucket {BucketId} with {FileCount} files, {ShortUrlCount} short URLs, {TokenCount} upload tokens",
    id, files.Count, shortUrls.Count, uploadTokens.Count);
```

When delete returns false:
```csharp
if (entity == null)
{
    _logger.LogDebug("Bucket {BucketId} not found for delete", id);
    return false;
}
if (!auth.CanManage(entity.Owner))
{
    _logger.LogWarning("Access denied: delete bucket {BucketId} by {Owner}", id, auth.OwnerName ?? "unknown");
    return false;
}
```

In `GetSummaryAsync` when not found:
```csharp
_logger.LogDebug("Bucket {BucketId} not found for summary", id);
```

**Step 2: Build and run tests**

Run: `dotnet build && dotnet test`
Expected: All pass

**Step 3: Commit**

```bash
git add src/CarbonFiles.Infrastructure/Services/BucketService.cs
git commit -m "feat: add logging to BucketService"
```

---

### Task 6: Add logging to FileService

**Files:**
- Modify: `src/CarbonFiles.Infrastructure/Services/FileService.cs`

**Step 1: Add ILogger to FileService**

Add `using Microsoft.Extensions.Logging;` and inject `ILogger<FileService>`:

```csharp
private readonly ILogger<FileService> _logger;

public FileService(CarbonFilesDbContext db, FileStorageService storage, INotificationService notifications, ILogger<FileService> logger)
{
    _db = db;
    _storage = storage;
    _notifications = notifications;
    _logger = logger;
}
```

Add log statements:

In `ListAsync`:
```csharp
_logger.LogDebug("Listing files in bucket {BucketId} (limit={Limit}, offset={Offset})", bucketId, pagination.Limit, pagination.Offset);
```

In `GetMetadataAsync` when not found:
```csharp
_logger.LogDebug("File {Path} not found in bucket {BucketId}", path, bucketId);
```

In `DeleteAsync` on success:
```csharp
_logger.LogInformation("Deleted file {Path} from bucket {BucketId}", normalized, bucketId);
```

In `DeleteAsync` on auth failure:
```csharp
if (!auth.CanManage(bucket.Owner))
{
    _logger.LogWarning("Access denied: delete file {Path} in bucket {BucketId}", path, bucketId);
    return false;
}
```

In `UpdateFileSizeAsync`:
```csharp
_logger.LogDebug("Updated file size for {Path} in bucket {BucketId}: {OldSize} -> {NewSize}", path, bucketId, oldSize, newSize);
```

**Step 2: Build and run tests**

Run: `dotnet build && dotnet test`
Expected: All pass

**Step 3: Commit**

```bash
git add src/CarbonFiles.Infrastructure/Services/FileService.cs
git commit -m "feat: add logging to FileService"
```

---

### Task 7: Add logging to UploadService

**Files:**
- Modify: `src/CarbonFiles.Infrastructure/Services/UploadService.cs`

**Step 1: Add ILogger to UploadService**

Add `using Microsoft.Extensions.Logging;` and inject `ILogger<UploadService>`:

```csharp
private readonly ILogger<UploadService> _logger;

public UploadService(CarbonFilesDbContext db, FileStorageService storage, INotificationService notifications, ILogger<UploadService> logger)
{
    _db = db;
    _storage = storage;
    _notifications = notifications;
    _logger = logger;
}
```

Add log statements:

At the start of `StoreFileAsync`:
```csharp
_logger.LogDebug("Storing file {Path} in bucket {BucketId}", path, bucketId);
```

After storing an existing file (update path):
```csharp
_logger.LogInformation("Updated file {Path} in bucket {BucketId} ({OldSize} -> {Size} bytes)", normalized, bucketId, oldSize, size);
```

After storing a new file:
```csharp
_logger.LogInformation("Created file {Path} in bucket {BucketId} ({Size} bytes, short code {ShortCode})", normalized, bucketId, size, shortCode);
```

**Step 2: Build and run tests**

Run: `dotnet build && dotnet test`
Expected: All pass

**Step 3: Commit**

```bash
git add src/CarbonFiles.Infrastructure/Services/UploadService.cs
git commit -m "feat: add logging to UploadService"
```

---

### Task 8: Add logging to FileStorageService

**Files:**
- Modify: `src/CarbonFiles.Infrastructure/Services/FileStorageService.cs`

**Step 1: Add ILogger to FileStorageService**

Add `using Microsoft.Extensions.Logging;` and inject `ILogger<FileStorageService>`:

```csharp
private readonly ILogger<FileStorageService> _logger;

public FileStorageService(IOptions<CarbonFilesOptions> options, ILogger<FileStorageService> logger)
{
    _dataDir = options.Value.DataDir;
    _logger = logger;
}
```

Add log statements:

In `StoreAsync` after `File.Move`:
```csharp
_logger.LogDebug("Stored {Size} bytes to {Path}", size, targetPath);
```

In `PatchFileAsync` after writing:
```csharp
_logger.LogDebug("Patched file at {Path} (append={Append}, offset={Offset}, new size={NewSize})", path, append, offset, fs.Length);
```

In `DeleteFile` when file is deleted:
```csharp
_logger.LogDebug("Deleted file at {Path}", path);
```

In `DeleteBucketDir` when directory is deleted:
```csharp
_logger.LogDebug("Deleted bucket directory {Dir}", dir);
```

**Step 2: Build and run tests**

Run: `dotnet build && dotnet test`
Expected: All pass

**Step 3: Commit**

```bash
git add src/CarbonFiles.Infrastructure/Services/FileStorageService.cs
git commit -m "feat: add logging to FileStorageService"
```

---

### Task 9: Add logging to ApiKeyService

**Files:**
- Modify: `src/CarbonFiles.Infrastructure/Services/ApiKeyService.cs`

**Step 1: Add ILogger to ApiKeyService**

Add `using Microsoft.Extensions.Logging;` and inject `ILogger<ApiKeyService>`:

```csharp
private readonly ILogger<ApiKeyService> _logger;

public ApiKeyService(CarbonFilesDbContext db, ILogger<ApiKeyService> logger)
{
    _db = db;
    _logger = logger;
}
```

Add log statements:

In `CreateAsync` after save:
```csharp
_logger.LogInformation("Created API key {Prefix} with name {Name}", prefix, name);
```

In `DeleteAsync` on success:
```csharp
_logger.LogInformation("Deleted API key {Prefix}", prefix);
```

In `DeleteAsync` when not found:
```csharp
_logger.LogDebug("API key {Prefix} not found for deletion", prefix);
```

In `GetUsageAsync` when not found:
```csharp
_logger.LogDebug("API key {Prefix} not found for usage query", prefix);
```

In `ValidateKeyAsync` when invalid:
```csharp
// After the format check fails
_logger.LogDebug("API key validation failed: invalid format");
```

After entity not found:
```csharp
_logger.LogDebug("API key validation failed: prefix {Prefix} not found", prefix);
```

After hash mismatch:
```csharp
_logger.LogWarning("API key validation failed: invalid secret for prefix {Prefix}", prefix);
```

**Step 2: Build and run tests**

Run: `dotnet build && dotnet test`
Expected: All pass

**Step 3: Commit**

```bash
git add src/CarbonFiles.Infrastructure/Services/ApiKeyService.cs
git commit -m "feat: add logging to ApiKeyService"
```

---

### Task 10: Add logging to remaining services

**Files:**
- Modify: `src/CarbonFiles.Infrastructure/Services/UploadTokenService.cs`
- Modify: `src/CarbonFiles.Infrastructure/Services/DashboardTokenService.cs`
- Modify: `src/CarbonFiles.Infrastructure/Services/ShortUrlService.cs`

**Step 1: Add ILogger to UploadTokenService**

Add `using Microsoft.Extensions.Logging;` and inject `ILogger<UploadTokenService>`:

```csharp
private readonly ILogger<UploadTokenService> _logger;

public UploadTokenService(CarbonFilesDbContext db, ILogger<UploadTokenService> logger)
{
    _db = db;
    _logger = logger;
}
```

In `CreateAsync` after save:
```csharp
_logger.LogInformation("Created upload token for bucket {BucketId} (expires {ExpiresAt}, max uploads {MaxUploads})",
    bucketId, entity.ExpiresAt.ToString("o"), request.MaxUploads?.ToString() ?? "unlimited");
```

In `ValidateAsync` when expired or exhausted:
```csharp
if (entity == null)
{
    _logger.LogDebug("Upload token not found");
    return (string.Empty, false);
}
if (entity.ExpiresAt <= DateTime.UtcNow)
{
    _logger.LogDebug("Upload token for bucket {BucketId} is expired", entity.BucketId);
    return (entity.BucketId, false);
}
if (entity.MaxUploads.HasValue && entity.UploadsUsed >= entity.MaxUploads.Value)
{
    _logger.LogDebug("Upload token for bucket {BucketId} exhausted ({Used}/{Max})", entity.BucketId, entity.UploadsUsed, entity.MaxUploads.Value);
    return (entity.BucketId, false);
}
```

**Step 2: Add ILogger to DashboardTokenService**

Add `using Microsoft.Extensions.Logging;` and inject `ILogger<DashboardTokenService>`:

```csharp
private readonly ILogger<DashboardTokenService> _logger;

public DashboardTokenService(JwtHelper jwt, ILogger<DashboardTokenService> logger)
{
    _jwt = jwt;
    _logger = logger;
}
```

In `CreateAsync` before return:
```csharp
_logger.LogInformation("Created dashboard token expiring at {ExpiresAt}", actualExpiry.ToString("o"));
```

In `ValidateToken` when invalid:
```csharp
if (!isValid)
{
    _logger.LogDebug("Dashboard token validation failed");
    return null;
}
```

**Step 3: Add ILogger to ShortUrlService**

Add `using Microsoft.Extensions.Logging;` and inject `ILogger<ShortUrlService>`:

```csharp
private readonly ILogger<ShortUrlService> _logger;

public ShortUrlService(CarbonFilesDbContext db, ILogger<ShortUrlService> logger)
{
    _db = db;
    _logger = logger;
}
```

In `CreateAsync` after save:
```csharp
_logger.LogInformation("Created short URL {Code} for bucket {BucketId} file {FilePath}", code, bucketId, filePath);
```

In `CreateAsync` when retrying collisions:
```csharp
_logger.LogDebug("Short code collision, retrying (attempt {Attempt})", attempt + 1);
```

In `ResolveAsync` when not found or expired:
```csharp
// When shortUrl is null
_logger.LogDebug("Short URL {Code} not found", code);
// When bucket expired
_logger.LogDebug("Short URL {Code} points to expired bucket {BucketId}", code, shortUrl.BucketId);
```

In `DeleteAsync` on success:
```csharp
_logger.LogInformation("Deleted short URL {Code}", code);
```

**Step 4: Build and run tests**

Run: `dotnet build && dotnet test`
Expected: All pass

**Step 5: Commit**

```bash
git add src/CarbonFiles.Infrastructure/Services/UploadTokenService.cs src/CarbonFiles.Infrastructure/Services/DashboardTokenService.cs src/CarbonFiles.Infrastructure/Services/ShortUrlService.cs
git commit -m "feat: add logging to UploadTokenService, DashboardTokenService, ShortUrlService"
```

---

### Task 11: Add logging to SignalR hub and notification service

**Files:**
- Modify: `src/CarbonFiles.Api/Hubs/FileHub.cs`
- Modify: `src/CarbonFiles.Api/Hubs/HubNotificationService.cs`

**Step 1: Add logging to FileHub**

Add `ILogger<FileHub>` injection and log connect/disconnect/subscribe events:

```csharp
using CarbonFiles.Core.Interfaces;
using Microsoft.AspNetCore.SignalR;

namespace CarbonFiles.Api.Hubs;

public class FileHub : Hub
{
    private readonly ILogger<FileHub> _logger;

    public FileHub(ILogger<FileHub> logger) => _logger = logger;

    public override Task OnConnectedAsync()
    {
        _logger.LogInformation("SignalR client connected: {ConnectionId}", Context.ConnectionId);
        return base.OnConnectedAsync();
    }

    public override Task OnDisconnectedAsync(Exception? exception)
    {
        if (exception != null)
            _logger.LogWarning(exception, "SignalR client disconnected with error: {ConnectionId}", Context.ConnectionId);
        else
            _logger.LogInformation("SignalR client disconnected: {ConnectionId}", Context.ConnectionId);
        return base.OnDisconnectedAsync(exception);
    }

    public async Task SubscribeToBucket(string bucketId)
    {
        _logger.LogDebug("Client {ConnectionId} subscribing to bucket {BucketId}", Context.ConnectionId, bucketId);
        await Groups.AddToGroupAsync(Context.ConnectionId, $"bucket:{bucketId}");
    }

    public async Task UnsubscribeFromBucket(string bucketId)
    {
        _logger.LogDebug("Client {ConnectionId} unsubscribing from bucket {BucketId}", Context.ConnectionId, bucketId);
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"bucket:{bucketId}");
    }

    public async Task SubscribeToFile(string bucketId, string path)
    {
        _logger.LogDebug("Client {ConnectionId} subscribing to file {BucketId}/{Path}", Context.ConnectionId, bucketId, path);
        await Groups.AddToGroupAsync(Context.ConnectionId, $"file:{bucketId}:{path}");
    }

    public async Task UnsubscribeFromFile(string bucketId, string path)
    {
        _logger.LogDebug("Client {ConnectionId} unsubscribing from file {BucketId}/{Path}", Context.ConnectionId, bucketId, path);
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"file:{bucketId}:{path}");
    }

    public async Task SubscribeToAll()
    {
        // Verify admin auth from query string token
        var httpContext = Context.GetHttpContext();
        var token = httpContext?.Request.Query["access_token"].FirstOrDefault();

        if (string.IsNullOrEmpty(token))
        {
            _logger.LogWarning("Client {ConnectionId} attempted global subscription without token", Context.ConnectionId);
            throw new HubException("Admin authentication required for global subscriptions");
        }

        var authService = httpContext!.RequestServices.GetRequiredService<IAuthService>();
        var auth = await authService.ResolveAsync(token);

        if (!auth.IsAdmin)
        {
            _logger.LogWarning("Client {ConnectionId} attempted global subscription with non-admin token", Context.ConnectionId);
            throw new HubException("Admin authentication required for global subscriptions");
        }

        _logger.LogInformation("Client {ConnectionId} subscribed to global notifications", Context.ConnectionId);
        await Groups.AddToGroupAsync(Context.ConnectionId, "global");
    }

    public async Task UnsubscribeFromAll()
    {
        _logger.LogDebug("Client {ConnectionId} unsubscribing from global notifications", Context.ConnectionId);
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, "global");
    }
}
```

**Step 2: Add logging to HubNotificationService**

Add `ILogger<HubNotificationService>`:

```csharp
using CarbonFiles.Core.Interfaces;
using CarbonFiles.Core.Models;
using CarbonFiles.Core.Models.Responses;
using Microsoft.AspNetCore.SignalR;

namespace CarbonFiles.Api.Hubs;

public sealed class HubNotificationService : INotificationService
{
    private readonly IHubContext<FileHub> _hub;
    private readonly ILogger<HubNotificationService> _logger;

    public HubNotificationService(IHubContext<FileHub> hub, ILogger<HubNotificationService> logger)
    {
        _hub = hub;
        _logger = logger;
    }

    public async Task NotifyFileCreated(string bucketId, BucketFile file)
    {
        _logger.LogDebug("Broadcasting FileCreated for {BucketId}/{Path}", bucketId, file.Path);
        await _hub.Clients.Group($"bucket:{bucketId}").SendAsync("FileCreated", bucketId, file);
        await _hub.Clients.Group($"file:{bucketId}:{file.Path}").SendAsync("FileCreated", bucketId, file);
        await _hub.Clients.Group("global").SendAsync("FileCreated", bucketId, file);
    }

    public async Task NotifyFileUpdated(string bucketId, BucketFile file)
    {
        _logger.LogDebug("Broadcasting FileUpdated for {BucketId}/{Path}", bucketId, file.Path);
        await _hub.Clients.Group($"bucket:{bucketId}").SendAsync("FileUpdated", bucketId, file);
        await _hub.Clients.Group($"file:{bucketId}:{file.Path}").SendAsync("FileUpdated", bucketId, file);
        await _hub.Clients.Group("global").SendAsync("FileUpdated", bucketId, file);
    }

    public async Task NotifyFileDeleted(string bucketId, string path)
    {
        _logger.LogDebug("Broadcasting FileDeleted for {BucketId}/{Path}", bucketId, path);
        await _hub.Clients.Group($"bucket:{bucketId}").SendAsync("FileDeleted", bucketId, path);
        await _hub.Clients.Group($"file:{bucketId}:{path}").SendAsync("FileDeleted", bucketId, path);
        await _hub.Clients.Group("global").SendAsync("FileDeleted", bucketId, path);
    }

    public async Task NotifyBucketCreated(Bucket bucket)
    {
        _logger.LogDebug("Broadcasting BucketCreated for {BucketId}", bucket.Id);
        await _hub.Clients.Group("global").SendAsync("BucketCreated", bucket);
    }

    public async Task NotifyBucketUpdated(string bucketId, BucketChanges changes)
    {
        _logger.LogDebug("Broadcasting BucketUpdated for {BucketId}", bucketId);
        await _hub.Clients.Group($"bucket:{bucketId}").SendAsync("BucketUpdated", bucketId, changes);
        await _hub.Clients.Group("global").SendAsync("BucketUpdated", bucketId, changes);
    }

    public async Task NotifyBucketDeleted(string bucketId)
    {
        _logger.LogDebug("Broadcasting BucketDeleted for {BucketId}", bucketId);
        await _hub.Clients.Group($"bucket:{bucketId}").SendAsync("BucketDeleted", bucketId);
        await _hub.Clients.Group("global").SendAsync("BucketDeleted", bucketId);
    }
}
```

**Step 3: Build and run tests**

Run: `dotnet build && dotnet test`
Expected: All pass

**Step 4: Commit**

```bash
git add src/CarbonFiles.Api/Hubs/FileHub.cs src/CarbonFiles.Api/Hubs/HubNotificationService.cs
git commit -m "feat: add logging to SignalR FileHub and HubNotificationService"
```

---

### Task 12: Add logging to endpoint handlers

**Files:**
- Modify: `src/CarbonFiles.Api/Endpoints/BucketEndpoints.cs`
- Modify: `src/CarbonFiles.Api/Endpoints/FileEndpoints.cs`
- Modify: `src/CarbonFiles.Api/Endpoints/KeyEndpoints.cs`
- Modify: `src/CarbonFiles.Api/Endpoints/UploadEndpoints.cs`
- Modify: `src/CarbonFiles.Api/Endpoints/UploadTokenEndpoints.cs`
- Modify: `src/CarbonFiles.Api/Endpoints/TokenEndpoints.cs`
- Modify: `src/CarbonFiles.Api/Endpoints/ShortUrlEndpoints.cs`
- Modify: `src/CarbonFiles.Api/Endpoints/StatsEndpoints.cs`
- Modify: `src/CarbonFiles.Api/Endpoints/HealthEndpoints.cs`

**General approach for endpoints:** Since endpoints are static extension methods with inline lambda handlers, inject `ILogger<Program>` (or a named logger via `ILoggerFactory`) into each handler via parameter DI. Use `ILoggerFactory` to create category-specific loggers.

**Implementation pattern:** For each endpoint file, add a `private static readonly string LogCategory` and inject `ILoggerFactory` into handlers that need logging. Create a logger with the category name.

Alternative (simpler): Inject `ILoggerFactory loggerFactory` and call `loggerFactory.CreateLogger("CarbonFiles.Api.Endpoints.BucketEndpoints")`.

**Step 1: Update BucketEndpoints.cs**

For each handler that needs logging, inject `ILoggerFactory loggerFactory` and create a logger at the top:

```csharp
var logger = loggerFactory.CreateLogger("CarbonFiles.Api.Endpoints.BucketEndpoints");
```

Add log statements to each endpoint:

**Create bucket handler:**
```csharp
logger.LogInformation("Bucket created: {BucketId} by {Owner}", result.Id, auth.IsAdmin ? "admin" : auth.OwnerName);
```
On error:
```csharp
logger.LogWarning("Bucket creation failed: {Error}", ex.Message);
```

**List buckets:** (debug only, services handle the detail)

**Get bucket:** When not found:
```csharp
logger.LogDebug("Bucket {BucketId} not found", id);
```

**Update bucket:** On success:
```csharp
logger.LogInformation("Bucket {BucketId} updated", id);
```

**Delete bucket:** On success:
```csharp
logger.LogInformation("Bucket {BucketId} deleted", id);
```

**ZIP download:** Already has a logger — keep the existing `ILogger<Program>` usage. Replace with `ILoggerFactory` for consistency:
```csharp
var logger = loggerFactory.CreateLogger("CarbonFiles.Api.Endpoints.BucketEndpoints");
```

Apply the same pattern to all other endpoint files:
- **KeyEndpoints**: Log key creation, deletion
- **UploadEndpoints**: Log upload completion with file count and sizes
- **UploadTokenEndpoints**: Log token creation
- **TokenEndpoints**: Log dashboard token creation
- **ShortUrlEndpoints**: Log short URL resolution, deletion
- **StatsEndpoints**: Log stats query
- **HealthEndpoints**: Log health check failures (Warning level)

**Step 2: Build and run tests**

Run: `dotnet build && dotnet test`
Expected: All pass

**Step 3: Commit**

```bash
git add src/CarbonFiles.Api/Endpoints/
git commit -m "feat: add logging to all endpoint handlers"
```

---

### Task 13: Final verification

**Step 1: Full build**

Run: `dotnet build`
Expected: Build succeeded, 0 warnings related to logging

**Step 2: Run all tests**

Run: `dotnet test`
Expected: All tests pass

**Step 3: Verify logging works locally**

Run: `dotnet run --project src/CarbonFiles.Api`

In another terminal, test a request:
```bash
curl -s http://localhost:5000/healthz | jq .
```

Expected: Console output shows structured log lines from RequestLoggingMiddleware, AuthMiddleware, and the health endpoint.

**Step 4: Final commit if any fixups needed**

```bash
git add -A
git commit -m "fix: resolve any logging build issues"
```
