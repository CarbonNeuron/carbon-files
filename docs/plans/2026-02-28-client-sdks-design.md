# Client SDK Generation Design

**Date**: 2026-02-28
**Status**: Approved

## Goal

Generate and publish "nice" API client SDKs for TypeScript, C#, Python, and PowerShell from CarbonFiles' OpenAPI spec. Clients should feel native to each language's ecosystem and be published to their respective package registries via CI/CD.

## Decisions

| Decision | Choice | Rationale |
|---|---|---|
| Repo strategy | Monorepo (`clients/` directory) | Single CI pipeline, atomic versioning, easier to keep in sync |
| TypeScript generator | Hey API (`@hey-api/openapi-ts`) | Named functions, tiny runtime (~4KB), excellent per-operation error types |
| C# generator | Refitter | Idiomatic Refit interface, System.Text.Json native, natural DI integration |
| Python generator | openapi-python-client | Modern Python 3.10+, httpx sync+async, ready-to-publish packages |
| PowerShell approach | Hand-crafted | Generators produce non-idiomatic output; hand-crafted gives pipeline support, -WhatIf, tab completion |
| Publish trigger | GitHub Release | Explicit version control via release tags (e.g., v1.2.0) |
| Package names | `@carbonfiles/client` (npm), `CarbonFiles.Client` (NuGet), `carbonfiles-client` (PyPI), `CarbonFiles` (PSGallery) |

## Repository Structure

```
clients/
├── typescript/                  # @carbonfiles/client (npm)
│   ├── src/
│   │   ├── client.gen.ts        # Generated client config
│   │   ├── types.gen.ts         # Generated request/response types
│   │   └── services.gen.ts      # Generated functions (one per endpoint)
│   ├── package.json
│   ├── tsconfig.json
│   └── hey-api.config.ts        # Generator config pointing to openapi.json
├── csharp/                      # CarbonFiles.Client (NuGet)
│   ├── CarbonFiles.Client.csproj
│   ├── .refitter                # Refitter config pointing to openapi.json
│   └── Generated/               # Generated Refit interface + model classes
├── python/                      # carbonfiles-client (PyPI)
│   ├── pyproject.toml
│   └── carbonfiles_client/      # Generated package
│       ├── client.py
│       ├── api/                  # One module per tag, 4 functions each
│       └── models/              # attrs-based model classes
└── powershell/                  # CarbonFiles (PSGallery)
    ├── CarbonFiles.psd1         # Module manifest
    ├── CarbonFiles.psm1         # Root module (dot-sources all functions)
    ├── Public/                  # Exported cmdlets (~20)
    └── Private/                 # Invoke-CfApiRequest, helpers
```

## CI/CD Pipeline

New workflow: `.github/workflows/publish-clients.yml`

**Trigger**: `on: release: types: [published]`

### Steps

1. **Build API & Export OpenAPI Spec**
   - Build the API project
   - Use `Microsoft.Extensions.ApiDescription.Server` or start API briefly to capture `/openapi/v1.json`
   - Save as `openapi.json` artifact

2. **Generate & Publish** (parallel jobs per language):

   **TypeScript**:
   - `npx @hey-api/openapi-ts` with `hey-api.config.ts`
   - Inject version from release tag into `package.json`
   - `npm publish --access public`

   **C#**:
   - `dotnet build clients/csharp/` (Refitter MSBuild generates at build time)
   - `dotnet pack` with version from release tag
   - `dotnet nuget push` to nuget.org

   **Python**:
   - `openapi-python-client generate --path openapi.json`
   - Inject version into `pyproject.toml`
   - `python -m build && twine upload dist/*`

   **PowerShell**:
   - Update `ModuleVersion` in `.psd1` manifest
   - `Publish-Module -Path ./clients/powershell -NuGetApiKey $key`

### Secrets Required

- `NPM_TOKEN` — npm publish token (scoped to `@carbonfiles`)
- `NUGET_API_KEY` — nuget.org API key
- `PYPI_API_TOKEN` — PyPI API token
- `PSGALLERY_API_KEY` — PowerShell Gallery API key

### Version Strategy

Version extracted from GitHub Release tag: `v1.2.0` → `1.2.0`. All four packages publish the same version number to stay in sync.

## TypeScript Client Design

**Package**: `@carbonfiles/client`
**Runtime dep**: `@hey-api/client-fetch` (~4KB, wraps native `fetch`)
**Works in**: Browsers, Node.js, Deno, Bun, Cloudflare Workers

### Consumer API

```typescript
import { client, createBucket, listFiles, uploadFiles } from '@carbonfiles/client';

client.setConfig({ baseUrl: 'https://files.example.com' });
client.interceptors.request.use((req) => {
  req.headers.set('Authorization', `Bearer ${token}`);
  return req;
});

const { data, error } = await createBucket({ body: { name: 'my-bucket' } });
if (error) {
  console.error(error.error, error.hint); // Typed error response
}
```

### Generated Output

- `types.gen.ts` — All request/response types from OpenAPI models
- `services.gen.ts` — One typed function per endpoint (e.g., `createBucket()`, `listFiles()`)
- `client.gen.ts` — Client configuration and interceptor setup

### Package Config

- ESM + CJS dual builds
- TypeScript declarations included
- `@hey-api/client-fetch` as peer dependency

## C# Client Design

**Package**: `CarbonFiles.Client`
**Runtime dep**: `Refit` (uses `System.Text.Json` by default)
**Targets**: `net10.0` + `netstandard2.0`

### Consumer API

```csharp
// DI registration
services.AddRefitClient<ICarbonFilesApi>()
    .ConfigureHttpClient(c => c.BaseAddress = new Uri("https://files.example.com"))
    .AddHttpMessageHandler<BearerTokenHandler>();

// Usage (injected)
var buckets = await api.ListBuckets(limit: 50);
var bucket = await api.CreateBucket(new CreateBucketRequest { Name = "my-bucket" });

// Error handling without exceptions
IApiResponse<Bucket> response = await api.GetBucket("abc123");
if (!response.IsSuccessStatusCode) {
    var error = response.Error; // Typed ErrorResponse
}
```

### Generated Output

- `ICarbonFilesApi.cs` — Refit interface with `[Get]`, `[Post]`, etc. attributes per endpoint
- Model classes for all request/response types
- `RefitSettings` configured for snake_case JSON

### Build Integration

Refitter MSBuild task regenerates the interface from `openapi.json` on every build. The `.refitter` config file controls namespace, interface name, and output path.

## Python Client Design

**Package**: `carbonfiles-client`
**Runtime deps**: `httpx`, `attrs`, `python-dateutil`
**Requires**: Python 3.10+

### Consumer API

```python
from carbonfiles_client import AuthenticatedClient
from carbonfiles_client.api.buckets import list_buckets, create_bucket
from carbonfiles_client.models import CreateBucketRequest

client = AuthenticatedClient(base_url="https://files.example.com", token="cf4_...")

# Sync
buckets = list_buckets.sync(client=client)

# Async
buckets = await list_buckets.asyncio(client=client)

# Detailed (full response with status code, headers)
response = create_bucket.sync_detailed(
    client=client,
    body=CreateBucketRequest(name="my-bucket"),
)
```

### Generated Output

- `client.py` — `Client` and `AuthenticatedClient` classes
- `api/` — One module per API tag, each with `sync()`, `sync_detailed()`, `asyncio()`, `asyncio_detailed()`
- `models/` — attrs-based model classes with type hints

### Package Config

Poetry-managed `pyproject.toml`. Published to PyPI via `twine`.

## PowerShell Module Design

**Package**: `CarbonFiles`
**Runtime deps**: PowerShell 7+ (no external modules)
**Noun prefix**: `Cf`

### Consumer API

```powershell
# Connect
Connect-CfServer -Uri "https://files.example.com" -Token "cf4_..."

# CRUD with pipeline support
$bucket = New-CfBucket -Name "my-bucket"
Get-CfBucket | Where-Object Name -like "temp*" | Remove-CfBucket -Confirm
Get-CfBucket -Id "abc123"

# File operations
Send-CfFile -BucketId $bucket.Id -FilePath ./data.zip
Get-CfFile -BucketId $bucket.Id | Save-CfFileContent -OutPath ./downloads/
Remove-CfBucket -Id "abc123" -WhatIf
```

### Cmdlet Inventory (~22 cmdlets)

| Cmdlet | HTTP | Description |
|---|---|---|
| `Connect-CfServer` | — | Set base URL + auth token (module-scoped) |
| `Disconnect-CfServer` | — | Clear connection state |
| `New-CfBucket` | POST /api/buckets | Create bucket |
| `Get-CfBucket` | GET /api/buckets[/{id}] | List or get single bucket |
| `Update-CfBucket` | PATCH /api/buckets/{id} | Update bucket properties |
| `Remove-CfBucket` | DELETE /api/buckets/{id} | Delete bucket (ShouldProcess) |
| `Get-CfBucketSummary` | GET /api/buckets/{id}/summary | Bucket summary/stats |
| `Save-CfBucketZip` | GET /api/buckets/{id}/zip | Download bucket as ZIP |
| `Get-CfFile` | GET /api/buckets/{id}/files[/{path}] | List or get single file |
| `Remove-CfFile` | DELETE /api/buckets/{id}/files/{path} | Delete file (ShouldProcess) |
| `Send-CfFile` | POST /api/buckets/{id}/upload | Upload file(s) |
| `Save-CfFileContent` | GET /api/buckets/{id}/files/{path} | Download file content |
| `New-CfApiKey` | POST /api/keys | Create API key |
| `Get-CfApiKey` | GET /api/keys | List API keys |
| `Remove-CfApiKey` | DELETE /api/keys/{prefix} | Delete API key (ShouldProcess) |
| `Get-CfApiKeyUsage` | GET /api/keys/{prefix}/usage | Get key usage stats |
| `New-CfUploadToken` | POST /api/buckets/{id}/tokens | Create upload token |
| `New-CfDashboardToken` | POST /api/tokens/dashboard | Create dashboard token |
| `Test-CfDashboardToken` | GET /api/tokens/dashboard/me | Validate dashboard token |
| `Get-CfStats` | GET /api/stats | System statistics |
| `Test-CfHealth` | GET /api/health | Health check |
| `Get-CfShortUrl` | GET /s/{code} | Resolve short URL |

### Internal Architecture

- `Private/Invoke-CfApiRequest.ps1` — Shared HTTP helper: injects auth header, calls `Invoke-RestMethod`, parses `{"error", "hint"}` responses into PowerShell errors, maps snake_case → PascalCase properties
- `Private/ConvertTo-PascalCase.ps1` — Recursively converts snake_case JSON keys to PascalCase on returned objects
- Module-scoped `$script:CfConnection` variable stores base URL, token, and default headers set by `Connect-CfServer`

### PowerShell Patterns

- All destructive cmdlets (`Remove-*`) implement `SupportsShouldProcess` for `-WhatIf`/`-Confirm`
- `Get-` cmdlets support both list and single-item via parameter sets (`-Id` for single)
- Pipeline input via `ValueFromPipelineByPropertyName` on `Id`/`BucketId` parameters
- Comment-based help on every exported function
- `[OutputType()]` attributes for IDE autocomplete
