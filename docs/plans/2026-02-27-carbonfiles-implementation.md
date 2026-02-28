# CarbonFiles API Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Build a complete file-sharing API with bucket-based organization, API key authentication, real-time events, and Native AOT compilation.

**Architecture:** ASP.NET Minimal API on .NET 10 with Clean Architecture (Api/Core/Infrastructure). EF Core + SQLite for metadata, filesystem for blob storage, SignalR for real-time events. Direct service pattern — no mediator, no CQRS.

**Tech Stack:** .NET 10 (SDK 10.0.103), EF Core 10.0.3 + SQLite, SignalR (JSON protocol only for AOT), Scalar.AspNetCore 2.11.0, System.Text.Json source generators, xUnit v3 (3.2.2) + FluentAssertions

**AOT Notes:**
- Use `WebApplication.CreateSlimBuilder(args)` instead of `CreateBuilder` for minimal AOT host
- SignalR: plain `Hub` only (no `Hub<T>` — causes runtime exceptions under AOT). Use `SendAsync` for all notifications.
- EF Core compiled models with `--precompile-queries --nativeaot` is still experimental in EF Core 10. Use standard `dotnet ef dbcontext optimize` for compiled models. Full AOT query precompilation may need workarounds.
- All `System.Text.Json` serialization must go through source-generated `JsonSerializerContext`. No reflection fallback under AOT.

**Package Versions (verified Feb 2026):**
| Package | Version |
|---------|---------|
| `Microsoft.EntityFrameworkCore.Sqlite` | 10.0.3 |
| `Microsoft.EntityFrameworkCore.Design` | 10.0.3 |
| `Scalar.AspNetCore` | 2.11.0 |
| `Microsoft.IdentityModel.JsonWebTokens` | 8.* |
| `xunit.v3` | 3.2.2 |
| `xunit.runner.visualstudio` | 3.1.5 |
| `FluentAssertions` | 8.8.0 |
| `Microsoft.AspNetCore.Mvc.Testing` | 10.* |

**Spec:** `docs/carbon-files-api.md`

---

## Phase 1: Project Scaffolding & Foundation

### Task 1: Create Solution and Project Structure

**Files:**
- Create: `CarbonFiles.sln`
- Create: `src/CarbonFiles.Api/CarbonFiles.Api.csproj`
- Create: `src/CarbonFiles.Core/CarbonFiles.Core.csproj`
- Create: `src/CarbonFiles.Infrastructure/CarbonFiles.Infrastructure.csproj`
- Create: `tests/CarbonFiles.Api.Tests/CarbonFiles.Api.Tests.csproj`
- Create: `tests/CarbonFiles.Core.Tests/CarbonFiles.Core.Tests.csproj`
- Create: `tests/CarbonFiles.Infrastructure.Tests/CarbonFiles.Infrastructure.Tests.csproj`

**Step 1: Create solution and projects**

```bash
# Create solution
dotnet new sln -n CarbonFiles

# Create projects
dotnet new web -n CarbonFiles.Api -o src/CarbonFiles.Api
dotnet new classlib -n CarbonFiles.Core -o src/CarbonFiles.Core
dotnet new classlib -n CarbonFiles.Infrastructure -o src/CarbonFiles.Infrastructure
dotnet new xunit -n CarbonFiles.Api.Tests -o tests/CarbonFiles.Api.Tests
dotnet new xunit -n CarbonFiles.Core.Tests -o tests/CarbonFiles.Core.Tests
dotnet new xunit -n CarbonFiles.Infrastructure.Tests -o tests/CarbonFiles.Infrastructure.Tests

# Add projects to solution
dotnet sln add src/CarbonFiles.Api
dotnet sln add src/CarbonFiles.Core
dotnet sln add src/CarbonFiles.Infrastructure
dotnet sln add tests/CarbonFiles.Api.Tests
dotnet sln add tests/CarbonFiles.Core.Tests
dotnet sln add tests/CarbonFiles.Infrastructure.Tests
```

**Step 2: Set up project references and dependencies**

Api.csproj references Core and Infrastructure. Infrastructure references Core. Test projects reference their targets.

`src/CarbonFiles.Api/CarbonFiles.Api.csproj`:
```xml
<Project Sdk="Microsoft.NET.Sdk.Web">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <PublishAot>true</PublishAot>
    <InvariantGlobalization>true</InvariantGlobalization>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\CarbonFiles.Core\CarbonFiles.Core.csproj" />
    <ProjectReference Include="..\CarbonFiles.Infrastructure\CarbonFiles.Infrastructure.csproj" />
  </ItemGroup>
  <ItemGroup>
    <PackageReference Include="Scalar.AspNetCore" Version="2.*" />
    <PackageReference Include="Microsoft.AspNetCore.OpenApi" Version="10.*" />
  </ItemGroup>
</Project>
```

`src/CarbonFiles.Core/CarbonFiles.Core.csproj`:
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>
</Project>
```

`src/CarbonFiles.Infrastructure/CarbonFiles.Infrastructure.csproj`:
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\CarbonFiles.Core\CarbonFiles.Core.csproj" />
  </ItemGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.EntityFrameworkCore.Sqlite" Version="10.*" />
    <PackageReference Include="Microsoft.EntityFrameworkCore.Design" Version="10.*">
      <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
      <PrivateAssets>all</PrivateAssets>
    </PackageReference>
    <PackageReference Include="Microsoft.IdentityModel.JsonWebTokens" Version="8.*" />
  </ItemGroup>
</Project>
```

Test projects:
```xml
<!-- Each test .csproj gets these packages -->
<PackageReference Include="FluentAssertions" Version="8.*" />
<PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.*" />
<PackageReference Include="xunit.v3" Version="3.*" />
<PackageReference Include="xunit.runner.visualstudio" Version="3.*">
  <PrivateAssets>all</PrivateAssets>
</PackageReference>
```

Api.Tests additionally needs:
```xml
<PackageReference Include="Microsoft.AspNetCore.Mvc.Testing" Version="10.*" />
<ProjectReference Include="..\..\src\CarbonFiles.Api\CarbonFiles.Api.csproj" />
```

**Step 3: Add project references**

```bash
dotnet add src/CarbonFiles.Api reference src/CarbonFiles.Core src/CarbonFiles.Infrastructure
dotnet add src/CarbonFiles.Infrastructure reference src/CarbonFiles.Core
dotnet add tests/CarbonFiles.Api.Tests reference src/CarbonFiles.Api
dotnet add tests/CarbonFiles.Core.Tests reference src/CarbonFiles.Core
dotnet add tests/CarbonFiles.Infrastructure.Tests reference src/CarbonFiles.Infrastructure
```

**Step 4: Verify build**

```bash
dotnet build
```
Expected: Build succeeded with 0 errors.

**Step 5: Commit**

```bash
git add -A
git commit -m "feat: scaffold solution with clean architecture projects"
```

---

### Task 2: Core Domain Models

**Files:**
- Create: `src/CarbonFiles.Core/Models/Bucket.cs`
- Create: `src/CarbonFiles.Core/Models/FileInfo.cs`
- Create: `src/CarbonFiles.Core/Models/ApiKey.cs`
- Create: `src/CarbonFiles.Core/Models/ShortUrl.cs`
- Create: `src/CarbonFiles.Core/Models/UploadToken.cs`
- Create: `src/CarbonFiles.Core/Models/AuthContext.cs`
- Create: `src/CarbonFiles.Core/Models/ErrorResponse.cs`
- Create: `src/CarbonFiles.Core/Models/PaginatedResponse.cs`
- Create: `src/CarbonFiles.Core/Models/PaginationParams.cs`

**Step 1: Create domain models**

These are the API response DTOs defined in the spec. NOT EF entities (those come later in Infrastructure).

`AuthContext.cs` — the resolved auth state per request:
```csharp
namespace CarbonFiles.Core.Models;

public sealed class AuthContext
{
    public AuthRole Role { get; init; }
    public string? OwnerName { get; init; }
    public string? KeyPrefix { get; init; }

    public static AuthContext Admin() => new() { Role = AuthRole.Admin };
    public static AuthContext Owner(string name, string prefix) => new() { Role = AuthRole.Owner, OwnerName = name, KeyPrefix = prefix };
    public static AuthContext Public() => new() { Role = AuthRole.Public };

    public bool IsAdmin => Role == AuthRole.Admin;
    public bool IsOwner => Role == AuthRole.Owner;
    public bool IsPublic => Role == AuthRole.Public;
    public bool CanManage(string bucketOwner) => IsAdmin || (IsOwner && OwnerName == bucketOwner);
}

public enum AuthRole { Public, Owner, Admin }
```

`Bucket.cs`:
```csharp
namespace CarbonFiles.Core.Models;

public sealed class Bucket
{
    public required string Id { get; init; }
    public required string Name { get; set; }
    public required string Owner { get; init; }
    public string? Description { get; set; }
    public DateTime CreatedAt { get; init; }
    public DateTime? ExpiresAt { get; set; }
    public DateTime? LastUsedAt { get; set; }
    public int FileCount { get; set; }
    public long TotalSize { get; set; }
}
```

`FileInfo.cs` (use `BucketFile` to avoid conflict with `System.IO.FileInfo`):
```csharp
namespace CarbonFiles.Core.Models;

public sealed class BucketFile
{
    public required string Path { get; init; }
    public required string Name { get; init; }
    public long Size { get; set; }
    public required string MimeType { get; init; }
    public string? ShortCode { get; set; }
    public string? ShortUrl { get; set; }
    public DateTime CreatedAt { get; init; }
    public DateTime UpdatedAt { get; set; }
}
```

`ErrorResponse.cs`:
```csharp
namespace CarbonFiles.Core.Models;

public sealed class ErrorResponse
{
    public required string Error { get; init; }
    public string? Hint { get; init; }
}
```

`PaginatedResponse.cs`:
```csharp
namespace CarbonFiles.Core.Models;

public sealed class PaginatedResponse<T>
{
    public required IReadOnlyList<T> Items { get; init; }
    public int Total { get; init; }
    public int Limit { get; init; }
    public int Offset { get; init; }
}
```

`PaginationParams.cs`:
```csharp
namespace CarbonFiles.Core.Models;

public sealed class PaginationParams
{
    public int Limit { get; init; } = 50;
    public int Offset { get; init; } = 0;
    public string Sort { get; init; } = "created_at";
    public string Order { get; init; } = "desc";
}
```

Also create request DTOs:
- Create: `src/CarbonFiles.Core/Models/Requests/CreateBucketRequest.cs`
- Create: `src/CarbonFiles.Core/Models/Requests/UpdateBucketRequest.cs`
- Create: `src/CarbonFiles.Core/Models/Requests/CreateApiKeyRequest.cs`
- Create: `src/CarbonFiles.Core/Models/Requests/CreateDashboardTokenRequest.cs`
- Create: `src/CarbonFiles.Core/Models/Requests/CreateUploadTokenRequest.cs`

And response DTOs:
- Create: `src/CarbonFiles.Core/Models/Responses/ApiKeyResponse.cs` (includes full key only on creation)
- Create: `src/CarbonFiles.Core/Models/Responses/ApiKeyListItem.cs`
- Create: `src/CarbonFiles.Core/Models/Responses/ApiKeyUsageResponse.cs`
- Create: `src/CarbonFiles.Core/Models/Responses/DashboardTokenResponse.cs`
- Create: `src/CarbonFiles.Core/Models/Responses/DashboardTokenInfo.cs`
- Create: `src/CarbonFiles.Core/Models/Responses/UploadTokenResponse.cs`
- Create: `src/CarbonFiles.Core/Models/Responses/UploadResponse.cs`
- Create: `src/CarbonFiles.Core/Models/Responses/BucketDetailResponse.cs` (extends Bucket with files array + has_more_files)
- Create: `src/CarbonFiles.Core/Models/Responses/HealthResponse.cs`
- Create: `src/CarbonFiles.Core/Models/Responses/StatsResponse.cs`
- Create: `src/CarbonFiles.Core/Models/Responses/BucketChanges.cs` (for SignalR)

Follow the spec exactly for field names and types. Use `System.Text.Json` attributes for snake_case:
```csharp
using System.Text.Json.Serialization;

// On each property:
[JsonPropertyName("created_at")]
public DateTime CreatedAt { get; init; }
```

**Step 2: Verify build**

```bash
dotnet build src/CarbonFiles.Core
```

**Step 3: Commit**

```bash
git add -A
git commit -m "feat: add core domain models and DTOs"
```

---

### Task 3: Core Interfaces and Utilities

**Files:**
- Create: `src/CarbonFiles.Core/Interfaces/IBucketService.cs`
- Create: `src/CarbonFiles.Core/Interfaces/IFileService.cs`
- Create: `src/CarbonFiles.Core/Interfaces/IApiKeyService.cs`
- Create: `src/CarbonFiles.Core/Interfaces/IUploadService.cs`
- Create: `src/CarbonFiles.Core/Interfaces/IShortUrlService.cs`
- Create: `src/CarbonFiles.Core/Interfaces/IDashboardTokenService.cs`
- Create: `src/CarbonFiles.Core/Interfaces/IAuthService.cs`
- Create: `src/CarbonFiles.Core/Utilities/IdGenerator.cs`
- Create: `src/CarbonFiles.Core/Utilities/MimeDetector.cs`
- Create: `src/CarbonFiles.Core/Utilities/ExpiryParser.cs`
- Test: `tests/CarbonFiles.Core.Tests/Utilities/IdGeneratorTests.cs`
- Test: `tests/CarbonFiles.Core.Tests/Utilities/MimeDetectorTests.cs`
- Test: `tests/CarbonFiles.Core.Tests/Utilities/ExpiryParserTests.cs`

**Step 1: Write failing tests for IdGenerator**

`IdGeneratorTests.cs`:
```csharp
public class IdGeneratorTests
{
    [Fact]
    public void GenerateBucketId_ReturnsAlphanumeric10Chars()
    {
        var id = IdGenerator.GenerateBucketId();
        id.Should().HaveLength(10);
        id.Should().MatchRegex("^[a-zA-Z0-9]+$");
    }

    [Fact]
    public void GenerateShortCode_ReturnsAlphanumeric6Chars()
    {
        var code = IdGenerator.GenerateShortCode();
        code.Should().HaveLength(6);
        code.Should().MatchRegex("^[a-zA-Z0-9]+$");
    }

    [Fact]
    public void GenerateApiKey_HasCorrectFormat()
    {
        var (full, prefix) = IdGenerator.GenerateApiKey();
        full.Should().StartWith("cf4_");
        prefix.Should().StartWith("cf4_");
        full.Should().Contain(prefix);
        // cf4_{8hex}_{32hex}
        full.Should().MatchRegex("^cf4_[a-f0-9]{8}_[a-f0-9]{32}$");
        prefix.Should().MatchRegex("^cf4_[a-f0-9]{8}$");
    }

    [Fact]
    public void GenerateUploadToken_StartsWithPrefix()
    {
        var token = IdGenerator.GenerateUploadToken();
        token.Should().StartWith("cfu_");
    }

    [Fact]
    public void GenerateBucketId_IsUnique()
    {
        var ids = Enumerable.Range(0, 1000).Select(_ => IdGenerator.GenerateBucketId()).ToHashSet();
        ids.Should().HaveCount(1000);
    }
}
```

**Step 2: Run tests to verify they fail**

```bash
dotnet test tests/CarbonFiles.Core.Tests --filter "IdGeneratorTests"
```
Expected: FAIL — `IdGenerator` doesn't exist yet.

**Step 3: Implement IdGenerator**

`IdGenerator.cs`:
```csharp
using System.Security.Cryptography;

namespace CarbonFiles.Core.Utilities;

public static class IdGenerator
{
    private const string AlphaNumeric = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";

    public static string GenerateBucketId() => GenerateRandomString(10, AlphaNumeric);
    public static string GenerateShortCode() => GenerateRandomString(6, AlphaNumeric);

    public static string GenerateUploadToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(24);
        return $"cfu_{Convert.ToHexStringLower(bytes)}";
    }

    public static (string FullKey, string Prefix) GenerateApiKey()
    {
        var prefixBytes = RandomNumberGenerator.GetBytes(4);
        var secretBytes = RandomNumberGenerator.GetBytes(16);
        var prefix = $"cf4_{Convert.ToHexStringLower(prefixBytes)}";
        var secret = Convert.ToHexStringLower(secretBytes);
        return ($"{prefix}_{secret}", prefix);
    }

    private static string GenerateRandomString(int length, string chars)
    {
        return string.Create(length, chars, static (span, c) =>
        {
            var bytes = RandomNumberGenerator.GetBytes(span.Length);
            for (int i = 0; i < span.Length; i++)
                span[i] = c[bytes[i] % c.Length];
        });
    }
}
```

**Step 4: Run tests to verify they pass**

```bash
dotnet test tests/CarbonFiles.Core.Tests --filter "IdGeneratorTests"
```

**Step 5: Write failing tests for MimeDetector**

```csharp
public class MimeDetectorTests
{
    [Theory]
    [InlineData("file.png", "image/png")]
    [InlineData("file.jpg", "image/jpeg")]
    [InlineData("file.jpeg", "image/jpeg")]
    [InlineData("file.gif", "image/gif")]
    [InlineData("file.svg", "image/svg+xml")]
    [InlineData("file.mp4", "video/mp4")]
    [InlineData("file.webm", "video/webm")]
    [InlineData("file.pdf", "application/pdf")]
    [InlineData("file.json", "application/json")]
    [InlineData("file.js", "text/javascript")]
    [InlineData("file.ts", "text/typescript")]
    [InlineData("file.html", "text/html")]
    [InlineData("file.css", "text/css")]
    [InlineData("file.md", "text/markdown")]
    [InlineData("file.rs", "text/x-rust")]
    [InlineData("file.cs", "text/x-csharp")]
    [InlineData("file.txt", "text/plain")]
    [InlineData("file.unknown", "application/octet-stream")]
    [InlineData("file", "application/octet-stream")]
    [InlineData("src/main.rs", "text/x-rust")]
    public void DetectMimeType_ReturnsCorrectType(string filename, string expected)
    {
        MimeDetector.DetectFromExtension(filename).Should().Be(expected);
    }
}
```

**Step 6: Implement MimeDetector**

Extension-based lookup with a `FrozenDictionary<string, string>` for O(1) lookups. Include all common MIME types.

**Step 7: Write failing tests for ExpiryParser**

```csharp
public class ExpiryParserTests
{
    [Theory]
    [InlineData("15m")]
    [InlineData("1h")]
    [InlineData("1d")]
    [InlineData("1w")]
    [InlineData("1m")]
    [InlineData("never")]
    public void Parse_DurationString_ReturnsExpectedExpiry(string input)
    {
        var result = ExpiryParser.Parse(input);
        if (input == "never")
            result.Should().BeNull();
        else
            result.Should().BeAfter(DateTime.UtcNow);
    }

    [Fact]
    public void Parse_UnixEpoch_ReturnsCorrectDateTime()
    {
        var epoch = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds();
        var result = ExpiryParser.Parse(epoch.ToString());
        result.Should().BeCloseTo(DateTime.UtcNow.AddHours(1), TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void Parse_Iso8601_ReturnsCorrectDateTime()
    {
        var dt = DateTime.UtcNow.AddDays(1);
        var result = ExpiryParser.Parse(dt.ToString("O"));
        result.Should().BeCloseTo(dt, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void Parse_Null_ReturnsDefaultOneWeek()
    {
        var result = ExpiryParser.Parse(null);
        result.Should().BeCloseTo(DateTime.UtcNow.AddDays(7), TimeSpan.FromSeconds(5));
    }
}
```

**Step 8: Implement ExpiryParser**

Detection logic: if value is all digits → Unix epoch. If contains `T` → ISO 8601. Otherwise → duration preset.

**Step 9: Run all Core tests**

```bash
dotnet test tests/CarbonFiles.Core.Tests
```

**Step 10: Create service interfaces**

Each interface defines the contract. Example `IBucketService.cs`:
```csharp
namespace CarbonFiles.Core.Interfaces;

public interface IBucketService
{
    Task<Bucket> CreateAsync(string name, string? description, string? expiresIn, AuthContext auth);
    Task<PaginatedResponse<Bucket>> ListAsync(PaginationParams pagination, AuthContext auth, bool includeExpired = false);
    Task<BucketDetailResponse?> GetByIdAsync(string id);
    Task<Bucket?> UpdateAsync(string id, string? name, string? description, string? expiresIn, AuthContext auth);
    Task<bool> DeleteAsync(string id, AuthContext auth);
    Task<Stream?> GetZipStreamAsync(string id, HttpResponse response);
    Task<string?> GetSummaryAsync(string id);
}
```

Define all interfaces similarly following the spec endpoints.

**Step 11: Commit**

```bash
git add -A
git commit -m "feat: add core interfaces, utilities with tests"
```

---

### Task 4: Configuration Options

**Files:**
- Create: `src/CarbonFiles.Core/Configuration/CarbonFilesOptions.cs`
- Create: `src/CarbonFiles.Api/appsettings.json`
- Create: `src/CarbonFiles.Api/appsettings.Development.json`

**Step 1: Create options class**

```csharp
namespace CarbonFiles.Core.Configuration;

public sealed class CarbonFilesOptions
{
    public const string SectionName = "CarbonFiles";

    public string AdminKey { get; set; } = string.Empty;
    public string? JwtSecret { get; set; }
    public string DataDir { get; set; } = "./data";
    public string DbPath { get; set; } = "./data/carbonfiles.db";
    public long MaxUploadSize { get; set; } = 0; // 0 = unlimited
    public int CleanupIntervalMinutes { get; set; } = 60;
    public string CorsOrigins { get; set; } = "*";

    public string EffectiveJwtSecret => JwtSecret ?? AdminKey;
}
```

**Step 2: Create appsettings files**

Per spec configuration section.

**Step 3: Commit**

```bash
git add -A
git commit -m "feat: add configuration options class and appsettings"
```

---

### Task 5: Infrastructure — EF Core Setup

**Files:**
- Create: `src/CarbonFiles.Infrastructure/Data/CarbonFilesDbContext.cs`
- Create: `src/CarbonFiles.Infrastructure/Data/Entities/BucketEntity.cs`
- Create: `src/CarbonFiles.Infrastructure/Data/Entities/FileEntity.cs`
- Create: `src/CarbonFiles.Infrastructure/Data/Entities/ApiKeyEntity.cs`
- Create: `src/CarbonFiles.Infrastructure/Data/Entities/ShortUrlEntity.cs`
- Create: `src/CarbonFiles.Infrastructure/Data/Entities/UploadTokenEntity.cs`
- Create: `src/CarbonFiles.Infrastructure/Data/EntityMapping.cs`

**Step 1: Create EF entities**

EF entities have internal fields not exposed in the API. Example `ApiKeyEntity`:
```csharp
namespace CarbonFiles.Infrastructure.Data.Entities;

public sealed class ApiKeyEntity
{
    public required string Prefix { get; set; }  // PK
    public required string HashedSecret { get; set; } // SHA-256 of secret portion
    public required string Name { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? LastUsedAt { get; set; }
}
```

`BucketEntity` has `OwnerKeyPrefix` FK to ApiKeyEntity. `FileEntity` has composite key (BucketId, Path). `ShortUrlEntity` has PK on Code with FK to BucketId.

**Step 2: Create DbContext with Fluent API configuration**

```csharp
public class CarbonFilesDbContext : DbContext
{
    public DbSet<BucketEntity> Buckets => Set<BucketEntity>();
    public DbSet<FileEntity> Files => Set<FileEntity>();
    public DbSet<ApiKeyEntity> ApiKeys => Set<ApiKeyEntity>();
    public DbSet<ShortUrlEntity> ShortUrls => Set<ShortUrlEntity>();
    public DbSet<UploadTokenEntity> UploadTokens => Set<UploadTokenEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<BucketEntity>(e =>
        {
            e.HasKey(b => b.Id);
            e.HasIndex(b => b.OwnerKeyPrefix);
            e.HasIndex(b => b.ExpiresAt);
        });

        modelBuilder.Entity<FileEntity>(e =>
        {
            e.HasKey(f => new { f.BucketId, f.Path });
            e.HasIndex(f => f.BucketId);
        });

        modelBuilder.Entity<ApiKeyEntity>(e =>
        {
            e.HasKey(k => k.Prefix);
        });

        modelBuilder.Entity<ShortUrlEntity>(e =>
        {
            e.HasKey(s => s.Code);
            e.HasIndex(s => new { s.BucketId, s.FilePath });
        });

        modelBuilder.Entity<UploadTokenEntity>(e =>
        {
            e.HasKey(t => t.Token);
            e.HasIndex(t => t.BucketId);
        });
    }
}
```

**Step 3: Create entity-to-model mapping extension methods**

`EntityMapping.cs` with `ToBucket()`, `ToBucketFile()`, etc.

**Step 4: Create initial migration**

```bash
dotnet ef migrations add InitialCreate --project src/CarbonFiles.Infrastructure --startup-project src/CarbonFiles.Api
```

Note: The Api project needs a minimal Program.cs first for this to work. Add a temporary one:
```csharp
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddDbContext<CarbonFilesDbContext>(options =>
    options.UseSqlite("Data Source=./data/carbonfiles.db"));
var app = builder.Build();
app.Run();
```

**Step 5: Generate compiled models for AOT**

```bash
dotnet ef dbcontext optimize --project src/CarbonFiles.Infrastructure --startup-project src/CarbonFiles.Api
```

**Step 6: Enable WAL mode on startup**

Add a startup task or extension method that runs `PRAGMA journal_mode=WAL;` on the SQLite connection.

**Step 7: Verify build**

```bash
dotnet build
```

**Step 8: Commit**

```bash
git add -A
git commit -m "feat: add EF Core entities, DbContext, initial migration"
```

---

### Task 6: JSON Serialization Context (AOT)

**Files:**
- Create: `src/CarbonFiles.Api/Serialization/CarbonFilesJsonContext.cs`

**Step 1: Create the source-generated JSON context**

Register ALL request/response types + SignalR payload types:

```csharp
using System.Text.Json.Serialization;
using CarbonFiles.Core.Models;
using CarbonFiles.Core.Models.Requests;
using CarbonFiles.Core.Models.Responses;

namespace CarbonFiles.Api.Serialization;

[JsonSerializable(typeof(Bucket))]
[JsonSerializable(typeof(BucketFile))]
[JsonSerializable(typeof(ErrorResponse))]
[JsonSerializable(typeof(PaginatedResponse<Bucket>))]
[JsonSerializable(typeof(PaginatedResponse<BucketFile>))]
[JsonSerializable(typeof(PaginatedResponse<ApiKeyListItem>))]
[JsonSerializable(typeof(BucketDetailResponse))]
[JsonSerializable(typeof(CreateBucketRequest))]
[JsonSerializable(typeof(UpdateBucketRequest))]
[JsonSerializable(typeof(CreateApiKeyRequest))]
[JsonSerializable(typeof(CreateDashboardTokenRequest))]
[JsonSerializable(typeof(CreateUploadTokenRequest))]
[JsonSerializable(typeof(ApiKeyResponse))]
[JsonSerializable(typeof(ApiKeyListItem))]
[JsonSerializable(typeof(ApiKeyUsageResponse))]
[JsonSerializable(typeof(DashboardTokenResponse))]
[JsonSerializable(typeof(DashboardTokenInfo))]
[JsonSerializable(typeof(UploadTokenResponse))]
[JsonSerializable(typeof(UploadResponse))]
[JsonSerializable(typeof(HealthResponse))]
[JsonSerializable(typeof(StatsResponse))]
[JsonSerializable(typeof(BucketChanges))]  // SignalR payload
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
internal partial class CarbonFilesJsonContext : JsonSerializerContext { }
```

**Important:** Using `SnakeCaseLower` naming policy means we don't need `[JsonPropertyName]` attributes on every property. All properties auto-convert to snake_case.

**Step 2: Verify build**

```bash
dotnet build src/CarbonFiles.Api
```

**Step 3: Commit**

```bash
git add -A
git commit -m "feat: add JSON source generation context for AOT"
```

---

### Task 7: Auth System

**Files:**
- Create: `src/CarbonFiles.Infrastructure/Auth/AuthService.cs`
- Create: `src/CarbonFiles.Infrastructure/Auth/JwtHelper.cs`
- Create: `src/CarbonFiles.Infrastructure/Auth/ApiKeyCacheService.cs`
- Create: `src/CarbonFiles.Api/Auth/AuthMiddleware.cs`
- Create: `src/CarbonFiles.Api/Auth/AuthExtensions.cs`
- Test: `tests/CarbonFiles.Core.Tests/Models/AuthContextTests.cs`
- Test: `tests/CarbonFiles.Infrastructure.Tests/Auth/AuthServiceTests.cs`
- Test: `tests/CarbonFiles.Infrastructure.Tests/Auth/JwtHelperTests.cs`

**Step 1: Write failing tests for AuthContext**

```csharp
public class AuthContextTests
{
    [Fact]
    public void Admin_CanManageAnyBucket()
    {
        var auth = AuthContext.Admin();
        auth.CanManage("anyone").Should().BeTrue();
        auth.IsAdmin.Should().BeTrue();
    }

    [Fact]
    public void Owner_CanManageOwnBuckets()
    {
        var auth = AuthContext.Owner("my-agent", "cf4_abc123");
        auth.CanManage("my-agent").Should().BeTrue();
        auth.CanManage("other-agent").Should().BeFalse();
    }

    [Fact]
    public void Public_CannotManageAnything()
    {
        var auth = AuthContext.Public();
        auth.CanManage("anyone").Should().BeFalse();
        auth.IsPublic.Should().BeTrue();
    }
}
```

**Step 2: Run tests — should pass since models exist from Task 2**

**Step 3: Write failing tests for JwtHelper**

Test JWT creation, validation, expiry enforcement, and 24h cap.

**Step 4: Implement JwtHelper**

Use `Microsoft.IdentityModel.JsonWebTokens` for AOT-compatible JWT handling:
```csharp
public sealed class JwtHelper
{
    private readonly byte[] _secretBytes;

    public JwtHelper(string secret)
    {
        _secretBytes = System.Text.Encoding.UTF8.GetBytes(secret);
    }

    public (string Token, DateTime ExpiresAt) CreateDashboardToken(DateTime expiresAt)
    {
        // Cap at 24 hours
        var maxExpiry = DateTime.UtcNow.AddHours(24);
        if (expiresAt > maxExpiry)
            throw new ArgumentException("Dashboard token expiry cannot exceed 24 hours");

        // Create JWT with HMAC-SHA256
        var handler = new JsonWebTokenHandler();
        var key = new SymmetricSecurityKey(_secretBytes);
        var descriptor = new SecurityTokenDescriptor
        {
            Claims = new Dictionary<string, object> { ["scope"] = "admin" },
            Expires = expiresAt,
            SigningCredentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256)
        };
        var token = handler.CreateToken(descriptor);
        return (token, expiresAt);
    }

    public bool ValidateToken(string token, out DateTime expiresAt) { /* ... */ }
}
```

**Step 5: Implement AuthService**

Resolves bearer tokens: admin key check → API key lookup (with MemoryCache, 30s TTL) → JWT validation → public.

**Step 6: Implement AuthMiddleware**

```csharp
public class AuthMiddleware
{
    private readonly RequestDelegate _next;

    public AuthMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context, IAuthService authService)
    {
        var token = context.Request.Headers.Authorization.FirstOrDefault()?.Replace("Bearer ", "");
        var authContext = token != null
            ? await authService.ResolveAsync(token)
            : AuthContext.Public();
        context.Items["AuthContext"] = authContext;
        await _next(context);
    }
}
```

Extension method to retrieve from HttpContext:
```csharp
public static class AuthExtensions
{
    public static AuthContext GetAuthContext(this HttpContext context)
        => context.Items["AuthContext"] as AuthContext ?? AuthContext.Public();

    public static IResult RequireAdmin(this HttpContext context)
        => context.GetAuthContext().IsAdmin ? null! : Results.Json(
            new ErrorResponse { Error = "Admin access required" }, statusCode: 403);

    public static IResult RequireAuth(this HttpContext context)
        => context.GetAuthContext().IsPublic ? Results.Json(
            new ErrorResponse { Error = "Authentication required" }, statusCode: 401) : null!;
}
```

**Step 7: Run all tests**

```bash
dotnet test
```

**Step 8: Commit**

```bash
git add -A
git commit -m "feat: add auth system with JWT, API key caching, middleware"
```

---

### Task 8: Program.cs — Wire Everything Up

**Files:**
- Modify: `src/CarbonFiles.Api/Program.cs`
- Create: `src/CarbonFiles.Infrastructure/DependencyInjection.cs`

**Step 1: Create Infrastructure DI extension**

```csharp
public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services, IConfiguration configuration)
    {
        var options = configuration.GetSection(CarbonFilesOptions.SectionName).Get<CarbonFilesOptions>()!;

        services.Configure<CarbonFilesOptions>(configuration.GetSection(CarbonFilesOptions.SectionName));

        services.AddDbContext<CarbonFilesDbContext>(opts =>
            opts.UseSqlite($"Data Source={options.DbPath}"));

        services.AddMemoryCache();
        services.AddSingleton<JwtHelper>(_ => new JwtHelper(options.EffectiveJwtSecret));
        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<IBucketService, BucketService>();
        services.AddScoped<IFileService, FileService>();
        services.AddScoped<IApiKeyService, ApiKeyService>();
        services.AddScoped<IUploadService, UploadService>();
        services.AddScoped<IShortUrlService, ShortUrlService>();
        services.AddScoped<IDashboardTokenService, DashboardTokenService>();
        services.AddHostedService<CleanupService>();

        return services;
    }
}
```

**Step 2: Write Program.cs**

```csharp
using CarbonFiles.Api.Auth;
using CarbonFiles.Api.Endpoints;
using CarbonFiles.Api.Hubs;
using CarbonFiles.Api.Serialization;
using CarbonFiles.Infrastructure;
using Scalar.AspNetCore;

var builder = WebApplication.CreateSlimBuilder(args);

// JSON serialization for AOT
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.TypeInfoResolverChain.Insert(0, CarbonFilesJsonContext.Default);
});

// SignalR
builder.Services.AddSignalR()
    .AddJsonProtocol(options =>
    {
        options.PayloadSerializerOptions.TypeInfoResolverChain.Insert(0, CarbonFilesJsonContext.Default);
    });

// OpenAPI
builder.Services.AddOpenApi();

// Infrastructure (EF Core, services, auth)
builder.Services.AddInfrastructure(builder.Configuration);

// CORS
var corsOrigins = builder.Configuration.GetValue<string>("CarbonFiles:CorsOrigins") ?? "*";
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        if (corsOrigins == "*")
            policy.AllowAnyOrigin();
        else
            policy.WithOrigins(corsOrigins.Split(',', StringSplitOptions.RemoveEmptyEntries));

        policy.AllowAnyMethod()
              .WithHeaders("Authorization", "Content-Type")
              .WithExposedHeaders("Content-Range", "Accept-Ranges", "Content-Length", "ETag", "Last-Modified");
    });
});

var app = builder.Build();

// Enable WAL mode + auto-migrate in development
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<CarbonFilesDbContext>();
    await db.Database.ExecuteSqlRawAsync("PRAGMA journal_mode=WAL;");
    if (app.Environment.IsDevelopment())
        await db.Database.MigrateAsync();
}

// Middleware
app.UseCors();
app.UseMiddleware<AuthMiddleware>();

// Endpoints
app.MapHealthEndpoints();
app.MapKeyEndpoints();
app.MapTokenEndpoints();
app.MapStatsEndpoints();
app.MapBucketEndpoints();
app.MapFileEndpoints();
app.MapUploadEndpoints();
app.MapShortUrlEndpoints();

// SignalR
app.MapHub<FileHub>("/hub/files");

// OpenAPI + Scalar
app.MapOpenApi();
app.MapScalarApiReference();

app.Run();

// Required for WebApplicationFactory in tests
public partial class Program { }
```

**Step 3: Verify build**

```bash
dotnet build
```

**Step 4: Commit**

```bash
git add -A
git commit -m "feat: wire up Program.cs with all middleware and DI"
```

---

## Phase 2: Core Endpoints

### Task 9: Health Endpoint

**Files:**
- Create: `src/CarbonFiles.Api/Endpoints/HealthEndpoints.cs`
- Test: `tests/CarbonFiles.Api.Tests/Endpoints/HealthEndpointTests.cs`
- Create: `tests/CarbonFiles.Api.Tests/TestFixture.cs`

**Step 1: Create test fixture**

```csharp
public class TestFixture : IAsyncLifetime
{
    public HttpClient Client { get; private set; } = null!;
    private WebApplicationFactory<Program> _factory = null!;
    private string _tempDir = null!;

    public async Task InitializeAsync()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"cf_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);

        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((ctx, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["CarbonFiles:AdminKey"] = "test-admin-key",
                    ["CarbonFiles:DataDir"] = _tempDir,
                    ["CarbonFiles:DbPath"] = Path.Combine(_tempDir, "test.db"),
                });
            });
        });

        Client = _factory.CreateClient();
    }

    public async Task DisposeAsync()
    {
        Client.Dispose();
        await _factory.DisposeAsync();
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    public void SetAuth(string token)
    {
        Client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
    }

    public void SetAdminAuth() => SetAuth("test-admin-key");
    public void ClearAuth() => Client.DefaultRequestHeaders.Authorization = null;
}
```

**Step 2: Write failing health endpoint test**

```csharp
public class HealthEndpointTests : IClassFixture<TestFixture>
{
    private readonly HttpClient _client;

    public HealthEndpointTests(TestFixture fixture) => _client = fixture.Client;

    [Fact]
    public async Task GetHealth_ReturnsHealthy()
    {
        var response = await _client.GetAsync("/healthz");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<HealthResponse>();
        body!.Status.Should().Be("healthy");
        body.Db.Should().Be("ok");
        body.UptimeSeconds.Should().BeGreaterOrEqualTo(0);
    }
}
```

**Step 3: Run test — should fail**

**Step 4: Implement HealthEndpoints**

```csharp
public static class HealthEndpoints
{
    private static readonly DateTime StartTime = DateTime.UtcNow;

    public static void MapHealthEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/healthz", async (CarbonFilesDbContext db) =>
        {
            try
            {
                await db.Database.CanConnectAsync();
                return Results.Ok(new HealthResponse
                {
                    Status = "healthy",
                    UptimeSeconds = (long)(DateTime.UtcNow - StartTime).TotalSeconds,
                    Db = "ok"
                });
            }
            catch
            {
                return Results.Json(
                    new HealthResponse { Status = "unhealthy", UptimeSeconds = 0, Db = "error" },
                    statusCode: 503);
            }
        });
    }
}
```

**Step 5: Run test — should pass**

**Step 6: Commit**

```bash
git add -A
git commit -m "feat: add health endpoint with integration test"
```

---

### Task 10: API Key Service & Endpoints

**Files:**
- Create: `src/CarbonFiles.Infrastructure/Services/ApiKeyService.cs`
- Create: `src/CarbonFiles.Api/Endpoints/KeyEndpoints.cs`
- Test: `tests/CarbonFiles.Api.Tests/Endpoints/KeyEndpointTests.cs`
- Test: `tests/CarbonFiles.Infrastructure.Tests/Services/ApiKeyServiceTests.cs`

**Step 1: Write failing unit tests for ApiKeyService**

Test create, list, delete, usage stats. Test that the hashed secret is stored (not plaintext). Test lookup by full key.

**Step 2: Implement ApiKeyService**

Key creation: generate key with `IdGenerator.GenerateApiKey()`, hash the secret with SHA-256, store prefix + hash + name.
Key lookup: extract prefix from the full key (`cf4_xxxxxxxx`), look up entity, verify hash matches.

```csharp
public sealed class ApiKeyService : IApiKeyService
{
    private readonly CarbonFilesDbContext _db;

    public async Task<ApiKeyResponse> CreateAsync(string name)
    {
        var (fullKey, prefix) = IdGenerator.GenerateApiKey();
        var secret = fullKey[(prefix.Length + 1)..]; // after "cf4_xxxxxxxx_"
        var hashed = HashSecret(secret);

        var entity = new ApiKeyEntity
        {
            Prefix = prefix,
            HashedSecret = hashed,
            Name = name,
            CreatedAt = DateTime.UtcNow
        };
        _db.ApiKeys.Add(entity);
        await _db.SaveChangesAsync();

        return new ApiKeyResponse
        {
            Key = fullKey,
            Prefix = prefix,
            Name = name,
            CreatedAt = entity.CreatedAt
        };
    }

    public async Task<ApiKeyEntity?> ValidateKeyAsync(string fullKey)
    {
        // cf4_{prefix}_{secret}
        var parts = fullKey.Split('_', 3);
        if (parts.Length != 3 || parts[0] != "cf4") return null;
        var prefix = $"cf4_{parts[1]}";
        var secret = parts[2];

        var entity = await _db.ApiKeys.FindAsync(prefix);
        if (entity == null) return null;

        var hashed = HashSecret(secret);
        return hashed == entity.HashedSecret ? entity : null;
    }

    private static string HashSecret(string secret)
    {
        var bytes = System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(secret));
        return Convert.ToHexStringLower(bytes);
    }
}
```

**Step 3: Write failing integration tests for Key endpoints**

Test all 4 endpoints: POST /api/keys (admin only), GET /api/keys (admin only), DELETE /api/keys/{prefix}, GET /api/keys/{prefix}/usage. Test 401/403 for non-admin.

**Step 4: Implement KeyEndpoints**

```csharp
public static class KeyEndpoints
{
    public static void MapKeyEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/keys");

        group.MapPost("/", async (CreateApiKeyRequest request, HttpContext ctx, IApiKeyService svc) =>
        {
            var auth = ctx.GetAuthContext();
            if (!auth.IsAdmin)
                return Results.Json(new ErrorResponse { Error = "Admin access required" }, statusCode: 403);

            var result = await svc.CreateAsync(request.Name);
            return Results.Created($"/api/keys/{result.Prefix}", result);
        });

        group.MapGet("/", async (HttpContext ctx, IApiKeyService svc,
            int limit = 50, int offset = 0, string sort = "created_at", string order = "desc") =>
        {
            var auth = ctx.GetAuthContext();
            if (!auth.IsAdmin)
                return Results.Json(new ErrorResponse { Error = "Admin access required" }, statusCode: 403);

            var result = await svc.ListAsync(new PaginationParams { Limit = limit, Offset = offset, Sort = sort, Order = order });
            return Results.Ok(result);
        });

        group.MapDelete("/{prefix}", async (string prefix, HttpContext ctx, IApiKeyService svc) =>
        {
            var auth = ctx.GetAuthContext();
            if (!auth.IsAdmin)
                return Results.Json(new ErrorResponse { Error = "Admin access required" }, statusCode: 403);

            var deleted = await svc.DeleteAsync(prefix);
            return deleted ? Results.NoContent() : Results.NotFound();
        });

        group.MapGet("/{prefix}/usage", async (string prefix, HttpContext ctx, IApiKeyService svc) =>
        {
            var auth = ctx.GetAuthContext();
            if (!auth.IsAdmin)
                return Results.Json(new ErrorResponse { Error = "Admin access required" }, statusCode: 403);

            var result = await svc.GetUsageAsync(prefix);
            return result != null ? Results.Ok(result) : Results.NotFound();
        });
    }
}
```

**Step 5: Run all tests**

```bash
dotnet test
```

**Step 6: Commit**

```bash
git add -A
git commit -m "feat: add API key management service and endpoints"
```

---

### Task 11: Bucket Service & Endpoints

**Files:**
- Create: `src/CarbonFiles.Infrastructure/Services/BucketService.cs`
- Create: `src/CarbonFiles.Api/Endpoints/BucketEndpoints.cs`
- Test: `tests/CarbonFiles.Api.Tests/Endpoints/BucketEndpointTests.cs`
- Test: `tests/CarbonFiles.Infrastructure.Tests/Services/BucketServiceTests.cs`

**Step 1: Write failing unit tests for BucketService**

Test: create bucket (with expiry parsing), list (admin sees all, owner sees own, expired excluded), get by ID (returns 404 for expired), update, delete (cascade deletes files from disk).

**Step 2: Implement BucketService**

Key behaviors:
- `CreateAsync`: generate 10-char ID, parse expiry, set owner from auth context
- `ListAsync`: filter by owner if not admin, exclude expired unless `includeExpired`
- `GetByIdAsync`: return null if expired, include first 100 files + `has_more_files`
- `DeleteAsync`: delete bucket dir from filesystem, remove DB records
- `UpdateAsync`: partial update of name/description/expires_at

**Step 3: Write failing integration tests**

Test all bucket endpoints with different auth levels. Test pagination, sorting, expired bucket behavior.

**Step 4: Implement BucketEndpoints**

POST /api/buckets, GET /api/buckets, GET /api/buckets/{id}, PATCH /api/buckets/{id}, DELETE /api/buckets/{id}

Note: ZIP and summary endpoints are separate tasks.

**Step 5: Run tests**

```bash
dotnet test
```

**Step 6: Commit**

```bash
git add -A
git commit -m "feat: add bucket CRUD service and endpoints"
```

---

### Task 12: File Storage Service

**Files:**
- Create: `src/CarbonFiles.Infrastructure/Services/FileStorageService.cs`
- Create: `src/CarbonFiles.Infrastructure/Services/FileService.cs`
- Test: `tests/CarbonFiles.Infrastructure.Tests/Services/FileStorageServiceTests.cs`

**Step 1: Write failing tests for file storage**

Test: store file to disk (correct path encoding), read file, delete file, atomic write (temp file + rename), directory creation, path encoding/decoding.

**Step 2: Implement FileStorageService**

Handles raw filesystem operations:
```csharp
public sealed class FileStorageService
{
    private readonly string _dataDir;

    public string GetFilePath(string bucketId, string filePath)
    {
        var encoded = Uri.EscapeDataString(filePath.ToLowerInvariant());
        return Path.Combine(_dataDir, bucketId, encoded);
    }

    public async Task<long> StoreAsync(string bucketId, string filePath, Stream content)
    {
        var targetPath = GetFilePath(bucketId, filePath);
        var dir = Path.GetDirectoryName(targetPath)!;
        Directory.CreateDirectory(dir);

        // Atomic write: temp file + rename
        var tempPath = targetPath + $".tmp.{Guid.NewGuid():N}";
        long size;
        await using (var fs = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            await content.CopyToAsync(fs);
            size = fs.Length;
        }
        File.Move(tempPath, targetPath, overwrite: true);
        return size;
    }

    public FileStream? OpenRead(string bucketId, string filePath)
    {
        var path = GetFilePath(bucketId, filePath);
        return File.Exists(path) ? new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read) : null;
    }

    public void DeleteBucketDir(string bucketId)
    {
        var dir = Path.Combine(_dataDir, bucketId);
        if (Directory.Exists(dir))
            Directory.Delete(dir, true);
    }
}
```

**Step 3: Implement FileService**

Handles DB metadata + delegates to FileStorageService for disk operations. Manages `FileEntity` records, short URL generation on file creation.

**Step 4: Run tests**

**Step 5: Commit**

```bash
git add -A
git commit -m "feat: add file storage service with atomic writes"
```

---

### Task 13: Upload Endpoints

**Files:**
- Create: `src/CarbonFiles.Infrastructure/Services/UploadService.cs`
- Create: `src/CarbonFiles.Api/Endpoints/UploadEndpoints.cs`
- Test: `tests/CarbonFiles.Api.Tests/Endpoints/UploadEndpointTests.cs`

**Step 1: Write failing integration tests**

Test multipart upload (single file, multiple files, custom field names), stream upload, auth checks (owner/admin/upload token), re-upload overwrites, MaxUploadSize enforcement.

**Step 2: Implement UploadService**

Handles both multipart and stream uploads:
- Multipart: parse `multipart/form-data` using `Request.ReadFormAsync()` with streaming. For each file section, determine path (generic name → original filename, custom name → field name as path). Stream to disk via FileStorageService. Create/update FileEntity + ShortUrl.
- Stream: read `Request.Body` directly to disk via `CopyToAsync`. Use `filename` query param for path.

Both check upload token validity if `?token=` present, increment `uploads_used` atomically.

**Step 3: Implement UploadEndpoints**

```csharp
public static class UploadEndpoints
{
    private static readonly HashSet<string> GenericFieldNames = new(StringComparer.OrdinalIgnoreCase)
        { "file", "files", "upload", "uploads", "blob" };

    public static void MapUploadEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/buckets/{id}/upload", async (string id, HttpContext ctx, IUploadService svc) =>
        {
            // Auth: owner, admin, or upload token
            // Stream multipart sections to disk
            // Return 201 with uploaded file list
        }).DisableAntiforgery();

        app.MapPut("/api/buckets/{id}/upload/stream", async (string id, HttpContext ctx, IUploadService svc) =>
        {
            // Auth: owner, admin, or upload token
            // Stream request body directly to disk
            // Return 201 with file info
        });
    }
}
```

**Step 4: Run tests**

```bash
dotnet test
```

**Step 5: Commit**

```bash
git add -A
git commit -m "feat: add multipart and stream upload endpoints"
```

---

### Task 14: File Metadata & Download Endpoints

**Files:**
- Create: `src/CarbonFiles.Api/Endpoints/FileEndpoints.cs`
- Test: `tests/CarbonFiles.Api.Tests/Endpoints/FileEndpointTests.cs`

**Step 1: Write failing integration tests**

Test: list files (paginated), get file metadata, download file content (correct headers, Content-Type, Content-Length, ETag, Last-Modified, Cache-Control), `?download=true` adds Content-Disposition, delete file (owner/admin only, cascade deletes short URL).

**Step 2: Implement FileEndpoints**

```csharp
public static class FileEndpoints
{
    public static void MapFileEndpoints(this IEndpointRouteBuilder app)
    {
        // List files in bucket
        app.MapGet("/api/buckets/{id}/files", async (string id, IFileService svc,
            int limit = 50, int offset = 0, string sort = "created_at", string order = "desc") =>
        {
            // Return paginated file list
        });

        // Get file metadata (catch-all route for paths with slashes)
        app.MapGet("/api/buckets/{id}/files/{*path}", async (string id, string path, IFileService svc) =>
        {
            // Return file metadata JSON
            // Route must NOT match /content suffix — handle routing carefully
        });

        // Download file content
        app.MapGet("/api/buckets/{id}/files/{*path}/content", ... /* see below */);

        // Delete file
        app.MapDelete("/api/buckets/{id}/files/{*path}", ...);
    }
}
```

**Important routing note:** The catch-all `{*path}` routes need careful ordering. The `/content` suffix route must be registered BEFORE the metadata route, or use route constraints. Alternatively, use a single catch-all and check if path ends with `/content`.

For file content download:
```csharp
app.MapGet("/api/buckets/{bucketId}/files/{*filePath}", async (
    string bucketId, string filePath, HttpContext ctx, IFileService svc) =>
{
    // Check if this is a /content request or metadata request
    if (filePath.EndsWith("/content"))
    {
        var actualPath = filePath[..^"/content".Length];
        return await ServeFileContent(bucketId, actualPath, ctx, svc);
    }
    // Otherwise return metadata
    return await GetFileMetadata(bucketId, filePath, svc);
});
```

File content response:
- Set `Content-Type` from MIME detection
- Set `Content-Length` from file size
- Set `Accept-Ranges: bytes`
- Set `ETag: "{size}-{lastModifiedTicks}"`
- Set `Last-Modified` header
- Set `Cache-Control: public, no-cache`
- Check `If-None-Match` → 304 if ETag matches
- Check `If-Modified-Since` → 304 if not modified
- Stream file content using `Results.File()` or manual `FileStream` write
- Support `?download=true` → add `Content-Disposition: attachment`
- Update `last_used_at` on bucket

**Step 3: Run tests**

**Step 4: Commit**

```bash
git add -A
git commit -m "feat: add file metadata, download, and delete endpoints"
```

---

### Task 15: Range Requests & Conditional Responses

**Files:**
- Modify: `src/CarbonFiles.Api/Endpoints/FileEndpoints.cs`
- Test: `tests/CarbonFiles.Api.Tests/Endpoints/RangeRequestTests.cs`

**Step 1: Write failing tests for range requests**

```csharp
public class RangeRequestTests : IClassFixture<TestFixture>
{
    [Fact]
    public async Task GetContent_WithRangeHeader_Returns206WithPartialContent()
    {
        // Upload a file, then request Range: bytes=0-9
        // Verify 206 status, Content-Range header, correct byte range
    }

    [Fact]
    public async Task GetContent_WithInvalidRange_Returns416()
    {
        // Request Range: bytes=999999-999999 on a small file
    }

    [Fact]
    public async Task GetContent_WithIfNoneMatch_Returns304()
    {
        // Upload file, get ETag, request with If-None-Match
    }

    [Fact]
    public async Task GetContent_WithIfModifiedSince_Returns304()
    {
        // Upload file, request with future If-Modified-Since
    }

    [Fact]
    public async Task GetContent_WithIfRange_ReturnsFullOrPartial()
    {
        // Test If-Range with valid and stale ETag
    }

    [Fact]
    public async Task HeadContent_ReturnsSameHeadersNoBody()
    {
        // HEAD request should return headers but empty body
    }
}
```

**Step 2: Implement range request handling**

Parse `Range: bytes=start-end` header. Seek in `FileStream`, write partial content. Set `Content-Range: bytes start-end/total` header. Return 206.

Use ASP.NET Core's built-in `PhysicalFileResult` or `FileStreamResult` which supports range requests natively — but verify it works correctly with AOT. If not, implement manually.

**Step 3: Run tests**

**Step 4: Commit**

```bash
git add -A
git commit -m "feat: add range requests and conditional response support"
```

---

### Task 16: Partial File Updates (PATCH content)

**Files:**
- Modify: `src/CarbonFiles.Api/Endpoints/FileEndpoints.cs`
- Modify: `src/CarbonFiles.Infrastructure/Services/FileService.cs`
- Test: `tests/CarbonFiles.Api.Tests/Endpoints/PatchContentTests.cs`

**Step 1: Write failing tests**

Test: overwrite byte range, append to file, invalid range (416), file not found (404), auth checks.

**Step 2: Implement PATCH /api/buckets/{id}/files/{path}/content**

Parse `Content-Range: bytes start-end/*` header. Open file with `FileShare.None` (exclusive lock), seek to offset, write request body. Or if `X-Append: true`, seek to end and append. Update file size and `updated_at` in DB.

**Step 3: Run tests**

**Step 4: Commit**

```bash
git add -A
git commit -m "feat: add partial file update (PATCH content) endpoint"
```

---

## Phase 3: Advanced Features

### Task 17: Short URL Service & Endpoints

**Files:**
- Create: `src/CarbonFiles.Infrastructure/Services/ShortUrlService.cs`
- Create: `src/CarbonFiles.Api/Endpoints/ShortUrlEndpoints.cs`
- Test: `tests/CarbonFiles.Api.Tests/Endpoints/ShortUrlEndpointTests.cs`
- Test: `tests/CarbonFiles.Infrastructure.Tests/Services/ShortUrlServiceTests.cs`

**Step 1: Write failing tests**

Test: short URL auto-generated on file upload, resolve redirects to content URL, 404 for expired bucket, delete short URL (owner/admin), collision handling.

**Step 2: Implement ShortUrlService**

- `CreateAsync(bucketId, filePath)`: generate 6-char code, check uniqueness, retry on collision
- `ResolveAsync(code)`: look up, verify bucket not expired, return redirect URL
- `DeleteAsync(code, auth)`: check ownership, delete

**Step 3: Implement ShortUrlEndpoints**

```csharp
app.MapGet("/s/{code}", async (string code, IShortUrlService svc) =>
{
    var url = await svc.ResolveAsync(code);
    return url != null ? Results.Redirect(url) : Results.NotFound();
});

app.MapDelete("/api/short/{code}", async (string code, HttpContext ctx, IShortUrlService svc) =>
{
    var auth = ctx.GetAuthContext();
    var deleted = await svc.DeleteAsync(code, auth);
    return deleted ? Results.NoContent() : Results.NotFound();
});
```

**Step 4: Run tests, commit**

```bash
git add -A
git commit -m "feat: add short URL generation, resolution, and deletion"
```

---

### Task 18: Upload Token Service & Endpoints

**Files:**
- Create: `src/CarbonFiles.Infrastructure/Services/UploadTokenService.cs`
- Create: `src/CarbonFiles.Api/Endpoints/UploadTokenEndpoints.cs` (or add to BucketEndpoints)
- Test: `tests/CarbonFiles.Api.Tests/Endpoints/UploadTokenEndpointTests.cs`

**Step 1: Write failing tests**

Test: create token (owner/admin only), use token for upload, token expiry, max_uploads enforcement, uploads_used increment.

**Step 2: Implement UploadTokenService**

- `CreateAsync(bucketId, expiresIn, maxUploads, auth)`: generate `cfu_` token, store in DB
- `ValidateAsync(token)`: check exists, not expired, uploads_used < max_uploads
- `IncrementUsageAsync(token, count)`: atomically increment uploads_used

**Step 3: Implement endpoint**

POST /api/buckets/{id}/tokens

**Step 4: Run tests, commit**

```bash
git add -A
git commit -m "feat: add upload token creation and validation"
```

---

### Task 19: Dashboard Token Service & Endpoints

**Files:**
- Create: `src/CarbonFiles.Infrastructure/Services/DashboardTokenService.cs`
- Create: `src/CarbonFiles.Api/Endpoints/TokenEndpoints.cs`
- Test: `tests/CarbonFiles.Api.Tests/Endpoints/DashboardTokenEndpointTests.cs`

**Step 1: Write failing tests**

Test: create token (admin only), validate token, expiry enforcement, 24h cap, use token for API access.

**Step 2: Implement DashboardTokenService**

Wraps `JwtHelper`. Parses `expires_in` using `ExpiryParser`, caps at 24h.

**Step 3: Implement TokenEndpoints**

POST /api/tokens/dashboard, GET /api/tokens/dashboard/me

**Step 4: Run tests, commit**

```bash
git add -A
git commit -m "feat: add dashboard JWT token creation and validation"
```

---

### Task 20: Stats Endpoint

**Files:**
- Create: `src/CarbonFiles.Api/Endpoints/StatsEndpoints.cs`
- Test: `tests/CarbonFiles.Api.Tests/Endpoints/StatsEndpointTests.cs`

**Step 1: Write failing tests**

Test: returns correct totals, storage_by_owner breakdown, admin-only access.

**Step 2: Implement StatsEndpoints**

Query aggregates from DB. Group by owner for `storage_by_owner`.

```csharp
app.MapGet("/api/stats", async (HttpContext ctx, CarbonFilesDbContext db) =>
{
    var auth = ctx.GetAuthContext();
    if (!auth.IsAdmin) return Results.Json(new ErrorResponse { Error = "Admin access required" }, statusCode: 403);

    var stats = new StatsResponse
    {
        TotalBuckets = await db.Buckets.CountAsync(b => b.ExpiresAt == null || b.ExpiresAt > DateTime.UtcNow),
        TotalFiles = await db.Files.CountAsync(),
        TotalSize = await db.Files.SumAsync(f => f.Size),
        TotalKeys = await db.ApiKeys.CountAsync(),
        TotalDownloads = await db.Buckets.SumAsync(b => b.DownloadCount),
        StorageByOwner = await db.Buckets
            .Where(b => b.ExpiresAt == null || b.ExpiresAt > DateTime.UtcNow)
            .GroupBy(b => b.Owner)
            .Select(g => new OwnerStats
            {
                Owner = g.Key,
                BucketCount = g.Count(),
                FileCount = g.Sum(b => b.FileCount),
                TotalSize = g.Sum(b => b.TotalSize)
            }).ToListAsync()
    };
    return Results.Ok(stats);
});
```

**Step 3: Run tests, commit**

```bash
git add -A
git commit -m "feat: add system stats endpoint"
```

---

## Phase 4: Real-Time & Background

### Task 21: SignalR Hub

**Files:**
- Create: `src/CarbonFiles.Api/Hubs/FileHub.cs`
- Modify: service classes to inject `IHubContext<FileHub>` and send events
- Test: `tests/CarbonFiles.Api.Tests/Hubs/FileHubTests.cs`

**Step 1: Implement FileHub**

```csharp
using Microsoft.AspNetCore.SignalR;

namespace CarbonFiles.Api.Hubs;

public class FileHub : Hub
{
    public async Task SubscribeToBucket(string bucketId)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, $"bucket:{bucketId}");
    }

    public async Task UnsubscribeFromBucket(string bucketId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"bucket:{bucketId}");
    }

    public async Task SubscribeToFile(string bucketId, string path)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, $"file:{bucketId}:{path}");
    }

    public async Task UnsubscribeFromFile(string bucketId, string path)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"file:{bucketId}:{path}");
    }

    public async Task SubscribeToAll()
    {
        // Verify admin auth
        var token = Context.GetHttpContext()?.Request.Query["access_token"].FirstOrDefault();
        // Resolve auth - must be admin
        // If not admin, throw HubException
        await Groups.AddToGroupAsync(Context.ConnectionId, "global");
    }

    public async Task UnsubscribeFromAll()
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, "global");
    }
}
```

**Step 2: Create notification helper**

```csharp
public sealed class HubNotificationService
{
    private readonly IHubContext<FileHub> _hub;

    public async Task NotifyFileCreated(string bucketId, BucketFile file)
    {
        await _hub.Clients.Group($"bucket:{bucketId}").SendAsync("FileCreated", bucketId, file);
        await _hub.Clients.Group($"file:{bucketId}:{file.Path}").SendAsync("FileCreated", bucketId, file);
    }

    public async Task NotifyFileUpdated(string bucketId, BucketFile file)
    {
        await _hub.Clients.Group($"bucket:{bucketId}").SendAsync("FileUpdated", bucketId, file);
        await _hub.Clients.Group($"file:{bucketId}:{file.Path}").SendAsync("FileUpdated", bucketId, file);
    }

    public async Task NotifyFileDeleted(string bucketId, string path)
    {
        await _hub.Clients.Group($"bucket:{bucketId}").SendAsync("FileDeleted", bucketId, path);
        await _hub.Clients.Group($"file:{bucketId}:{path}").SendAsync("FileDeleted", bucketId, path);
    }

    public async Task NotifyBucketCreated(Bucket bucket)
    {
        await _hub.Clients.Group("global").SendAsync("BucketCreated", bucket);
    }

    public async Task NotifyBucketUpdated(string bucketId, BucketChanges changes)
    {
        await _hub.Clients.Group($"bucket:{bucketId}").SendAsync("BucketUpdated", bucketId, changes);
        await _hub.Clients.Group("global").SendAsync("BucketUpdated", bucketId, changes);
    }

    public async Task NotifyBucketDeleted(string bucketId)
    {
        await _hub.Clients.Group($"bucket:{bucketId}").SendAsync("BucketDeleted", bucketId);
        await _hub.Clients.Group("global").SendAsync("BucketDeleted", bucketId);
    }
}
```

**Step 3: Wire notifications into existing services**

Add `HubNotificationService` calls after: file upload, file delete, file update, bucket create, bucket update, bucket delete.

**Step 4: Write integration tests**

Use SignalR test client (`HubConnectionBuilder` pointing at test server) to verify events are sent.

**Step 5: Run tests, commit**

```bash
git add -A
git commit -m "feat: add SignalR hub with real-time file and bucket events"
```

---

### Task 22: Background Cleanup Service

**Files:**
- Create: `src/CarbonFiles.Infrastructure/Services/CleanupService.cs`
- Test: `tests/CarbonFiles.Infrastructure.Tests/Services/CleanupServiceTests.cs`

**Step 1: Write failing tests**

Test: expired buckets are cleaned up, files deleted from disk, DB records removed, configurable interval.

**Step 2: Implement CleanupService**

```csharp
public sealed class CleanupService : BackgroundService
{
    private readonly IServiceProvider _provider;
    private readonly IOptions<CarbonFilesOptions> _options;
    private readonly ILogger<CleanupService> _logger;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromMinutes(_options.Value.CleanupIntervalMinutes), stoppingToken);

            using var scope = _provider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<CarbonFilesDbContext>();
            var storage = scope.ServiceProvider.GetRequiredService<FileStorageService>();

            var expired = await db.Buckets
                .Where(b => b.ExpiresAt != null && b.ExpiresAt < DateTime.UtcNow)
                .ToListAsync(stoppingToken);

            foreach (var bucket in expired)
            {
                _logger.LogInformation("Cleaning up expired bucket {BucketId}", bucket.Id);
                storage.DeleteBucketDir(bucket.Id);

                // Delete associated records
                var files = await db.Files.Where(f => f.BucketId == bucket.Id).ToListAsync(stoppingToken);
                var shortUrls = await db.ShortUrls.Where(s => s.BucketId == bucket.Id).ToListAsync(stoppingToken);
                var tokens = await db.UploadTokens.Where(t => t.BucketId == bucket.Id).ToListAsync(stoppingToken);

                db.Files.RemoveRange(files);
                db.ShortUrls.RemoveRange(shortUrls);
                db.UploadTokens.RemoveRange(tokens);
                db.Buckets.Remove(bucket);
            }

            if (expired.Count > 0)
                await db.SaveChangesAsync(stoppingToken);
        }
    }
}
```

**Step 3: Run tests, commit**

```bash
git add -A
git commit -m "feat: add background cleanup service for expired buckets"
```

---

### Task 23: ZIP Streaming & Bucket Summary

**Files:**
- Modify: `src/CarbonFiles.Infrastructure/Services/BucketService.cs`
- Modify: `src/CarbonFiles.Api/Endpoints/BucketEndpoints.cs`
- Test: `tests/CarbonFiles.Api.Tests/Endpoints/BucketZipTests.cs`
- Test: `tests/CarbonFiles.Api.Tests/Endpoints/BucketSummaryTests.cs`

**Step 1: Write failing tests for ZIP download**

Test: ZIP contains all files, correct Content-Disposition header, streams without buffering, HEAD request returns headers only.

**Step 2: Implement ZIP streaming**

```csharp
app.MapGet("/api/buckets/{id}/zip", async (string id, HttpContext ctx, IBucketService svc) =>
{
    var bucket = await svc.GetBucketEntityAsync(id);
    if (bucket == null) return Results.NotFound();

    ctx.Response.ContentType = "application/zip";
    ctx.Response.Headers["Content-Disposition"] = $"attachment; filename=\"{bucket.Name}.zip\"";

    // Log warning for large buckets
    if (bucket.FileCount > 10000 || bucket.TotalSize > 10L * 1024 * 1024 * 1024)
        logger.LogWarning("Large ZIP request for bucket {Id}: {Count} files, {Size} bytes", id, bucket.FileCount, bucket.TotalSize);

    using var archive = new ZipArchive(ctx.Response.BodyWriter.AsStream(), ZipArchiveMode.Create, leaveOpen: true);
    // Stream each file into the archive
    await foreach (var file in svc.GetFilesStreamAsync(id))
    {
        var entry = archive.CreateEntry(file.Path);
        await using var entryStream = entry.Open();
        await using var fileStream = storage.OpenRead(id, file.Path);
        if (fileStream != null)
            await fileStream.CopyToAsync(entryStream);
    }

    return Results.Empty;
});
```

**Step 3: Write failing tests for bucket summary**

Test: returns plaintext, includes bucket name, file listing, total size.

**Step 4: Implement bucket summary**

```csharp
app.MapGet("/api/buckets/{id}/summary", async (string id, IBucketService svc) =>
{
    var summary = await svc.GetSummaryAsync(id);
    return summary != null ? Results.Text(summary, "text/plain") : Results.NotFound();
});
```

Summary format:
```
Bucket: my-project
Owner: claude-agent
Files: 42 (1.2 MB)
Created: 2026-02-27
Expires: 2026-03-06

Files:
  src/main.rs (1.2 KB)
  README.md (3.4 KB)
  ...
```

**Step 5: Run tests, commit**

```bash
git add -A
git commit -m "feat: add ZIP streaming and plaintext bucket summary"
```

---

## Phase 5: Packaging & Documentation

### Task 24: Docker & Docker Compose

**Files:**
- Create: `Dockerfile`
- Create: `docker-compose.yml`
- Create: `.dockerignore`

**Step 1: Create Dockerfile**

```dockerfile
# Build stage — AOT compile
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY . .
RUN dotnet restore
RUN dotnet publish src/CarbonFiles.Api -c Release -o /app/publish

# Runtime — no .NET runtime needed, just the native binary
FROM mcr.microsoft.com/dotnet/runtime-deps:10.0-noble-chiseled
WORKDIR /app
COPY --from=build /app/publish .
EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080
ENTRYPOINT ["./CarbonFiles.Api"]
```

**Step 2: Create docker-compose.yml**

```yaml
services:
  api:
    build: .
    ports:
      - "8080:8080"
    volumes:
      - ./data:/app/data
    environment:
      - CarbonFiles__AdminKey=change-me-in-production
      - CarbonFiles__DataDir=/app/data
      - CarbonFiles__DbPath=/app/data/carbonfiles.db
```

**Step 3: Create .dockerignore**

```
**/bin
**/obj
**/data
**/.git
**/node_modules
```

**Step 4: Test Docker build locally (if Docker available)**

```bash
docker build -t carbonfiles .
```

**Step 5: Commit**

```bash
git add -A
git commit -m "feat: add Dockerfile and docker-compose for deployment"
```

---

### Task 25: CI/CD GitHub Actions

**Files:**
- Create: `.github/workflows/ci.yml`

**Step 1: Create CI workflow**

```yaml
name: CI

on:
  push:
    branches: [main]
  pull_request:
    branches: [main]

jobs:
  build-and-test:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4

      - name: Setup .NET
        uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '10.0.x'

      - name: Restore
        run: dotnet restore

      - name: Build
        run: dotnet build --no-restore

      - name: Test
        run: dotnet test --no-build --verbosity normal

  publish:
    needs: build-and-test
    if: github.ref == 'refs/heads/main'
    runs-on: ubuntu-latest
    permissions:
      packages: write
      contents: read
    steps:
      - uses: actions/checkout@v4

      - name: Setup .NET
        uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '10.0.x'

      - name: Publish AOT
        run: dotnet publish src/CarbonFiles.Api -c Release -o publish

      - name: Login to GHCR
        uses: docker/login-action@v3
        with:
          registry: ghcr.io
          username: ${{ github.actor }}
          password: ${{ secrets.GITHUB_TOKEN }}

      - name: Build and push Docker image
        uses: docker/build-push-action@v5
        with:
          context: .
          push: true
          tags: |
            ghcr.io/${{ github.repository }}:latest
            ghcr.io/${{ github.repository }}:${{ github.sha }}
```

**Step 2: Commit**

```bash
git add -A
git commit -m "feat: add GitHub Actions CI/CD workflow"
```

---

### Task 26: README

**Files:**
- Create: `README.md`

**Step 1: Write README**

Include all sections from the spec: what it is, quick start (Docker), API overview with curl examples, configuration table, migration workflow, development setup, architecture diagram, license.

Architecture diagram (text-based):
```
┌──────────────────────────────────────────┐
│                 Clients                   │
│  (curl, frontend, LLM agents, SDKs)     │
└──────────────┬───────────────────────────┘
               │ HTTP / WebSocket
┌──────────────▼───────────────────────────┐
│           CarbonFiles.Api                │
│  ┌─────────┐ ┌──────────┐ ┌──────────┐  │
│  │Endpoints│ │Auth Middleware│ │SignalR│  │
│  └────┬────┘ └──────────┘ └──────────┘  │
└───────┼──────────────────────────────────┘
        │
┌───────▼──────────────────────────────────┐
│       CarbonFiles.Infrastructure         │
│  ┌─────────┐ ┌──────────┐ ┌──────────┐  │
│  │Services │ │EF Core DB│ │File Store│  │
│  └─────────┘ └────┬─────┘ └────┬─────┘  │
└────────────────────┼────────────┼────────┘
                     │            │
              ┌──────▼──┐  ┌─────▼─────┐
              │ SQLite  │  │ Filesystem│
              └─────────┘  └───────────┘
```

**Step 2: Commit**

```bash
git add -A
git commit -m "docs: add comprehensive README with API docs and setup guide"
```

---

## Final Verification

### Task 27: Full Test Suite & AOT Verification

**Step 1: Run complete test suite**

```bash
dotnet test --verbosity normal
```
Expected: All tests pass.

**Step 2: Verify AOT build**

```bash
dotnet publish src/CarbonFiles.Api -c Release
```
Expected: Successful native AOT compilation.

**Step 3: Smoke test**

```bash
# Start the app
./src/CarbonFiles.Api/bin/Release/net10.0/publish/CarbonFiles.Api &

# Health check
curl http://localhost:5000/healthz

# Create API key
curl -X POST http://localhost:5000/api/keys \
  -H "Authorization: Bearer test-admin-key" \
  -H "Content-Type: application/json" \
  -d '{"name":"test"}'

# Kill the app
kill %1
```

**Step 4: Final commit**

```bash
git add -A
git commit -m "chore: final verification - all tests pass, AOT builds"
```

---

## Summary

| Phase | Tasks | Focus |
|-------|-------|-------|
| 1 | 1-8 | Scaffolding, models, config, auth, Program.cs |
| 2 | 9-16 | Health, keys, buckets, files, uploads, range requests |
| 3 | 17-20 | Short URLs, upload tokens, dashboard tokens, stats |
| 4 | 21-23 | SignalR, cleanup, ZIP/summary |
| 5 | 24-27 | Docker, CI/CD, README, verification |

Total: 27 tasks. Each task is one focused feature with tests.
