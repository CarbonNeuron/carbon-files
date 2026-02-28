# Client SDK Generation Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Generate and publish API client SDKs for TypeScript, C#, Python, and PowerShell from the CarbonFiles OpenAPI spec, with CI/CD publishing on GitHub Release.

**Architecture:** Each client lives under `clients/{language}/` in the monorepo. Three clients are auto-generated from the OpenAPI spec (TypeScript via Hey API, C# via Refitter, Python via openapi-python-client). PowerShell is hand-crafted. A single GitHub Actions workflow exports the spec, generates clients, and publishes to npm/NuGet/PyPI/PSGallery on release.

**Tech Stack:** Hey API (`@hey-api/openapi-ts`), Refitter + Refit, openapi-python-client, PowerShell 7+, GitHub Actions

---

### Task 1: Export OpenAPI Spec at Build Time

**Files:**
- Create: `scripts/export-openapi.sh`

**Step 1: Create the spec export script**

This script builds the API, starts it briefly, downloads the OpenAPI spec, and stops it. This is used by both local development and CI.

```bash
#!/usr/bin/env bash
set -euo pipefail

OUTPUT="${1:-openapi.json}"

dotnet build src/CarbonFiles.Api --configuration Release --verbosity quiet

# Start the API in the background with a temp database
export CarbonFiles__DbPath="/tmp/carbonfiles-openapi-$$.db"
export CarbonFiles__DataDir="/tmp/carbonfiles-openapi-$$"
export CarbonFiles__AdminKey="openapi-export-key"
mkdir -p "$CarbonFiles__DataDir"

dotnet run --project src/CarbonFiles.Api --configuration Release --no-build &
API_PID=$!

cleanup() {
  kill "$API_PID" 2>/dev/null || true
  rm -rf "$CarbonFiles__DbPath" "$CarbonFiles__DataDir"
}
trap cleanup EXIT

# Wait for the API to start
for i in $(seq 1 30); do
  if curl -sf http://localhost:5000/healthz > /dev/null 2>&1; then
    break
  fi
  sleep 1
done

# Download the spec
curl -sf http://localhost:5000/openapi/v1.json -o "$OUTPUT"
echo "OpenAPI spec exported to $OUTPUT"
```

**Step 2: Make it executable and test it**

Run: `chmod +x scripts/export-openapi.sh && ./scripts/export-openapi.sh openapi.json`
Expected: `openapi.json` created with valid OpenAPI 3.x content. Verify with `cat openapi.json | python3 -m json.tool | head -20`.

**Step 3: Commit**

```bash
git add scripts/export-openapi.sh
git commit -m "feat: add OpenAPI spec export script"
```

---

### Task 2: TypeScript Client Setup

**Files:**
- Create: `clients/typescript/openapi-ts.config.ts`
- Create: `clients/typescript/package.json`
- Create: `clients/typescript/tsconfig.json`
- Create: `clients/typescript/.gitignore`

**Step 1: Create the directory and package.json**

```bash
mkdir -p clients/typescript/src
```

Create `clients/typescript/package.json`:

```json
{
  "name": "@carbonfiles/client",
  "version": "0.0.0",
  "description": "TypeScript client for the CarbonFiles API",
  "type": "module",
  "main": "./dist/index.js",
  "types": "./dist/index.d.ts",
  "exports": {
    ".": {
      "types": "./dist/index.d.ts",
      "default": "./dist/index.js"
    }
  },
  "files": [
    "dist"
  ],
  "scripts": {
    "generate": "openapi-ts",
    "build": "tsc",
    "prepublishOnly": "npm run build"
  },
  "license": "MIT",
  "repository": {
    "type": "git",
    "url": "https://github.com/CarbonNeuron/carbon-files.git",
    "directory": "clients/typescript"
  },
  "devDependencies": {
    "@hey-api/openapi-ts": "0.93.0",
    "typescript": "~5.8.0"
  }
}
```

**Step 2: Create tsconfig.json**

Create `clients/typescript/tsconfig.json`:

```json
{
  "compilerOptions": {
    "target": "ES2022",
    "module": "NodeNext",
    "moduleResolution": "NodeNext",
    "declaration": true,
    "declarationMap": true,
    "sourceMap": true,
    "outDir": "./dist",
    "rootDir": "./src",
    "strict": true,
    "esModuleInterop": true,
    "skipLibCheck": true,
    "isolatedModules": true,
    "verbatimModuleSyntax": true
  },
  "include": ["src"],
  "exclude": ["node_modules", "dist"]
}
```

**Step 3: Create openapi-ts.config.ts**

Create `clients/typescript/openapi-ts.config.ts`:

```typescript
import { defineConfig } from '@hey-api/openapi-ts';

export default defineConfig({
  input: './openapi.json',
  output: {
    path: './src/client',
  },
  plugins: [
    {
      name: '@hey-api/typescript',
      enums: false,
    },
    {
      name: '@hey-api/sdk',
    },
    {
      name: '@hey-api/client-fetch',
    },
  ],
});
```

**Step 4: Create src/index.ts re-export**

Create `clients/typescript/src/index.ts`:

```typescript
export * from './client/index.js';
```

**Step 5: Create .gitignore**

Create `clients/typescript/.gitignore`:

```
node_modules/
dist/
src/client/
openapi.json
```

**Step 6: Test generation**

Run:
```bash
cp openapi.json clients/typescript/openapi.json
cd clients/typescript && npm install && npm run generate
```
Expected: Files generated in `clients/typescript/src/client/` — `types.gen.ts`, `sdk.gen.ts`, `client.gen.ts`, `index.ts`.

**Step 7: Test build**

Run: `npm run build`
Expected: `dist/` directory created with `.js` and `.d.ts` files. No TypeScript errors.

**Step 8: Commit**

```bash
git add clients/typescript/package.json clients/typescript/tsconfig.json clients/typescript/openapi-ts.config.ts clients/typescript/src/index.ts clients/typescript/.gitignore
git commit -m "feat: add TypeScript client (Hey API) scaffolding"
```

---

### Task 3: C# Client Setup

**Files:**
- Create: `clients/csharp/CarbonFiles.Client.csproj`
- Create: `clients/csharp/.refitter`
- Create: `clients/csharp/CarbonFilesClientExtensions.cs`
- Create: `clients/csharp/.gitignore`

**Step 1: Create the directory**

```bash
mkdir -p clients/csharp/Generated
```

**Step 2: Create CarbonFiles.Client.csproj**

Create `clients/csharp/CarbonFiles.Client.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFrameworks>net10.0;netstandard2.0</TargetFrameworks>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <LangVersion>latest</LangVersion>

    <!-- NuGet package metadata -->
    <PackageId>CarbonFiles.Client</PackageId>
    <Version>0.0.0</Version>
    <Authors>CarbonFiles</Authors>
    <Description>Generated C# client for the CarbonFiles API</Description>
    <PackageTags>carbonfiles;api;client;refit</PackageTags>
    <PackageLicenseExpression>MIT</PackageLicenseExpression>
    <RepositoryUrl>https://github.com/CarbonNeuron/carbon-files</RepositoryUrl>

    <!-- Refitter: disable telemetry -->
    <RefitterNoLogging>true</RefitterNoLogging>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Refitter.MSBuild" Version="1.6.*">
      <PrivateAssets>all</PrivateAssets>
    </PackageReference>
    <PackageReference Include="Refit" Version="8.*" />
    <PackageReference Include="Refit.HttpClientFactory" Version="8.*" />
  </ItemGroup>

</Project>
```

**Step 3: Create .refitter config**

Create `clients/csharp/.refitter`:

```json
{
  "openApiPath": "./openapi.json",
  "namespace": "CarbonFiles.Client",
  "naming": {
    "useOpenApiTitle": false,
    "interfaceName": "CarbonFilesApi"
  },
  "generateContracts": true,
  "generateXmlDocCodeComments": true,
  "generateStatusCodeComments": true,
  "addAutoGeneratedHeader": true,
  "addAcceptHeaders": true,
  "useCancellationTokens": true,
  "typeAccessibility": "Public",
  "outputFolder": "./Generated",
  "outputFilename": "CarbonFilesClient.cs",
  "codeGeneratorSettings": {
    "generateNullableReferenceTypes": true
  }
}
```

**Step 4: Create convenience extension method**

Create `clients/csharp/CarbonFilesClientExtensions.cs`:

```csharp
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.DependencyInjection;
using Refit;

namespace CarbonFiles.Client;

/// <summary>
/// Extension methods for registering the CarbonFiles API client.
/// </summary>
public static class CarbonFilesClientExtensions
{
    /// <summary>
    /// Adds the CarbonFiles API client to the service collection.
    /// </summary>
    public static IHttpClientBuilder AddCarbonFilesClient(
        this IServiceCollection services,
        Uri baseAddress)
    {
        var jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        var refitSettings = new RefitSettings
        {
            ContentSerializer = new SystemTextJsonContentSerializer(jsonOptions)
        };

        return services
            .AddRefitClient<ICarbonFilesApi>(refitSettings)
            .ConfigureHttpClient(c => c.BaseAddress = baseAddress);
    }
}
```

**Step 5: Create .gitignore**

Create `clients/csharp/.gitignore`:

```
bin/
obj/
Generated/
openapi.json
```

**Step 6: Test generation and build**

Run:
```bash
cp openapi.json clients/csharp/openapi.json
dotnet build clients/csharp/CarbonFiles.Client.csproj --configuration Release
```
Expected: Build succeeds. Generated files appear in `clients/csharp/Generated/`.

**Step 7: Test pack**

Run: `dotnet pack clients/csharp/CarbonFiles.Client.csproj --configuration Release`
Expected: `.nupkg` file created in `clients/csharp/bin/Release/`.

**Step 8: Commit**

```bash
git add clients/csharp/CarbonFiles.Client.csproj clients/csharp/.refitter clients/csharp/CarbonFilesClientExtensions.cs clients/csharp/.gitignore
git commit -m "feat: add C# client (Refitter/Refit) scaffolding"
```

---

### Task 4: Python Client Setup

**Files:**
- Create: `clients/python/config.yml`
- Create: `clients/python/generate.sh`
- Create: `clients/python/.gitignore`

**Step 1: Create the directory and config**

```bash
mkdir -p clients/python
```

Create `clients/python/config.yml`:

```yaml
project_name_override: "carbonfiles-client"
package_name_override: "carbonfiles_client"
package_version_override: "0.0.0"
post_hooks:
  - "ruff check . --fix-only"
  - "ruff format ."
```

**Step 2: Create the generation script**

Create `clients/python/generate.sh`:

```bash
#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
SPEC="${1:-$SCRIPT_DIR/openapi.json}"

cd "$SCRIPT_DIR"

# Generate into a temp directory, then move the package contents
openapi-python-client generate \
  --path "$SPEC" \
  --config config.yml \
  --meta poetry \
  --output-path ./generated \
  --overwrite

# Move the generated package directory into place
rm -rf carbonfiles_client
cp -r generated/carbonfiles_client .
cp generated/pyproject.toml .
cp generated/README.md . 2>/dev/null || true
rm -rf generated

echo "Python client generated successfully"
```

**Step 3: Create .gitignore**

Create `clients/python/.gitignore`:

```
__pycache__/
*.pyc
dist/
build/
*.egg-info/
generated/
carbonfiles_client/
pyproject.toml
openapi.json
```

**Step 4: Test generation**

Run:
```bash
pip install openapi-python-client
chmod +x clients/python/generate.sh
cp openapi.json clients/python/openapi.json
clients/python/generate.sh
```
Expected: `clients/python/carbonfiles_client/` directory created with `client.py`, `api/`, `models/` subdirectories. `pyproject.toml` created.

**Step 5: Test build**

Run:
```bash
cd clients/python && pip install build && python -m build
```
Expected: `dist/` directory with `.tar.gz` and `.whl` files.

**Step 6: Commit**

```bash
git add clients/python/config.yml clients/python/generate.sh clients/python/.gitignore
git commit -m "feat: add Python client (openapi-python-client) scaffolding"
```

---

### Task 5: PowerShell Module — Core Infrastructure

**Files:**
- Create: `clients/powershell/CarbonFiles.psd1`
- Create: `clients/powershell/CarbonFiles.psm1`
- Create: `clients/powershell/Private/Invoke-CfApiRequest.ps1`
- Create: `clients/powershell/Private/ConvertTo-PascalCaseKeys.ps1`

**Step 1: Create the directory structure**

```bash
mkdir -p clients/powershell/Public clients/powershell/Private
```

**Step 2: Create the module manifest**

Create `clients/powershell/CarbonFiles.psd1`:

```powershell
@{
    RootModule        = 'CarbonFiles.psm1'
    ModuleVersion     = '0.0.0'
    GUID              = 'a1b2c3d4-e5f6-7890-abcd-ef1234567890'
    Author            = 'CarbonFiles'
    Description       = 'PowerShell client for the CarbonFiles API'
    PowerShellVersion = '7.0'
    FunctionsToExport = @(
        'Connect-CfServer'
        'Disconnect-CfServer'
        'New-CfBucket'
        'Get-CfBucket'
        'Update-CfBucket'
        'Remove-CfBucket'
        'Get-CfBucketSummary'
        'Save-CfBucketZip'
        'Get-CfFile'
        'Remove-CfFile'
        'Send-CfFile'
        'Save-CfFileContent'
        'New-CfApiKey'
        'Get-CfApiKey'
        'Remove-CfApiKey'
        'Get-CfApiKeyUsage'
        'New-CfUploadToken'
        'New-CfDashboardToken'
        'Test-CfDashboardToken'
        'Get-CfStats'
        'Test-CfHealth'
    )
    CmdletsToExport   = @()
    VariablesToExport  = @()
    AliasesToExport    = @()
    PrivateData       = @{
        PSData = @{
            Tags         = @('CarbonFiles', 'API', 'FileSharing', 'REST')
            LicenseUri   = 'https://github.com/CarbonNeuron/carbon-files/blob/main/LICENSE'
            ProjectUri   = 'https://github.com/CarbonNeuron/carbon-files'
        }
    }
}
```

Note: Generate a real GUID before committing (run `[guid]::NewGuid()` in PowerShell).

**Step 3: Create the root module**

Create `clients/powershell/CarbonFiles.psm1`:

```powershell
# Dot-source all private functions
Get-ChildItem -Path "$PSScriptRoot/Private/*.ps1" -ErrorAction SilentlyContinue |
    ForEach-Object { . $_.FullName }

# Dot-source all public functions
Get-ChildItem -Path "$PSScriptRoot/Public/*.ps1" -ErrorAction SilentlyContinue |
    ForEach-Object { . $_.FullName }

# Module-scoped connection state
$script:CfConnection = @{
    BaseUri = $null
    Token   = $null
    Headers = @{}
}
```

**Step 4: Create the shared HTTP helper**

Create `clients/powershell/Private/Invoke-CfApiRequest.ps1`:

```powershell
function Invoke-CfApiRequest {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [Microsoft.PowerShell.Commands.WebRequestMethod]$Method,

        [Parameter(Mandatory)]
        [string]$Path,

        [Parameter()]
        [hashtable]$Body,

        [Parameter()]
        [hashtable]$Query,

        [Parameter()]
        [hashtable]$ExtraHeaders,

        [Parameter()]
        [string]$OutFile,

        [Parameter()]
        [string]$ContentType,

        [Parameter()]
        [object]$RawBody
    )

    if (-not $script:CfConnection.BaseUri) {
        throw 'Not connected. Run Connect-CfServer first.'
    }

    $uri = "$($script:CfConnection.BaseUri.TrimEnd('/'))$Path"

    # Append query parameters
    if ($Query -and $Query.Count -gt 0) {
        $pairs = $Query.GetEnumerator() | Where-Object { $null -ne $_.Value } |
            ForEach-Object { "$([uri]::EscapeDataString($_.Key))=$([uri]::EscapeDataString($_.Value))" }
        if ($pairs) {
            $uri += "?$($pairs -join '&')"
        }
    }

    $params = @{
        Method  = $Method
        Uri     = $uri
        Headers = $script:CfConnection.Headers.Clone()
    }

    if ($ExtraHeaders) {
        foreach ($key in $ExtraHeaders.Keys) {
            $params.Headers[$key] = $ExtraHeaders[$key]
        }
    }

    if ($Body) {
        $params.Body = $Body | ConvertTo-Json -Depth 10
        $params.ContentType = 'application/json'
    }

    if ($RawBody) {
        $params.Body = $RawBody
        if ($ContentType) { $params.ContentType = $ContentType }
    }

    if ($OutFile) {
        $params.OutFile = $OutFile
    }

    try {
        $response = Invoke-RestMethod @params -ErrorAction Stop
        if ($response -is [System.Management.Automation.PSObject]) {
            ConvertTo-PascalCaseKeys $response
        }
        else {
            $response
        }
    }
    catch {
        $err = $_.ErrorDetails.Message | ConvertFrom-Json -ErrorAction SilentlyContinue
        if ($err.error) {
            $msg = $err.error
            if ($err.hint) { $msg += " (Hint: $($err.hint))" }
            $ex = [System.InvalidOperationException]::new($msg)
            $record = [System.Management.Automation.ErrorRecord]::new(
                $ex, 'CarbonFilesApiError', 'InvalidOperation', $uri)
            $PSCmdlet.ThrowTerminatingError($record)
        }
        else {
            throw
        }
    }
}
```

**Step 5: Create the snake_case to PascalCase converter**

Create `clients/powershell/Private/ConvertTo-PascalCaseKeys.ps1`:

```powershell
function ConvertTo-PascalCaseKeys {
    [CmdletBinding()]
    [OutputType([PSCustomObject])]
    param(
        [Parameter(Mandatory, ValueFromPipeline)]
        [object]$InputObject
    )

    process {
        if ($InputObject -is [System.Collections.IList]) {
            $InputObject | ForEach-Object { ConvertTo-PascalCaseKeys $_ }
            return
        }

        if ($InputObject -isnot [System.Management.Automation.PSObject]) {
            return $InputObject
        }

        $result = [ordered]@{}
        foreach ($prop in $InputObject.PSObject.Properties) {
            $pascalName = ($prop.Name -split '_' | ForEach-Object {
                if ($_) { $_.Substring(0,1).ToUpper() + $_.Substring(1) }
            }) -join ''

            $value = if ($prop.Value -is [System.Management.Automation.PSObject] -or
                         $prop.Value -is [System.Collections.IList]) {
                ConvertTo-PascalCaseKeys $prop.Value
            } else {
                $prop.Value
            }
            $result[$pascalName] = $value
        }
        [PSCustomObject]$result
    }
}
```

**Step 6: Test the module structure**

Run: `pwsh -Command "Test-ModuleManifest clients/powershell/CarbonFiles.psd1"`
Expected: Manifest is valid (no errors).

**Step 7: Commit**

```bash
git add clients/powershell/
git commit -m "feat: add PowerShell module core infrastructure"
```

---

### Task 6: PowerShell Module — Connection Cmdlets

**Files:**
- Create: `clients/powershell/Public/Connect-CfServer.ps1`
- Create: `clients/powershell/Public/Disconnect-CfServer.ps1`

**Step 1: Create Connect-CfServer**

Create `clients/powershell/Public/Connect-CfServer.ps1`:

```powershell
function Connect-CfServer {
    <#
    .SYNOPSIS
        Connects to a CarbonFiles server.
    .DESCRIPTION
        Sets the base URI and authentication token for subsequent CarbonFiles cmdlet calls.
    .PARAMETER Uri
        The base URI of the CarbonFiles server (e.g., https://files.example.com).
    .PARAMETER Token
        The Bearer token for authentication (admin key, API key cf4_*, or dashboard JWT).
    .EXAMPLE
        Connect-CfServer -Uri "https://files.example.com" -Token "cf4_myapikey"
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory, Position = 0)]
        [ValidateNotNullOrEmpty()]
        [uri]$Uri,

        [Parameter(Mandatory, Position = 1)]
        [ValidateNotNullOrEmpty()]
        [string]$Token
    )

    $script:CfConnection.BaseUri = $Uri.ToString().TrimEnd('/')
    $script:CfConnection.Token = $Token
    $script:CfConnection.Headers = @{
        Authorization = "Bearer $Token"
    }

    Write-Verbose "Connected to $($script:CfConnection.BaseUri)"
}
```

**Step 2: Create Disconnect-CfServer**

Create `clients/powershell/Public/Disconnect-CfServer.ps1`:

```powershell
function Disconnect-CfServer {
    <#
    .SYNOPSIS
        Disconnects from the CarbonFiles server.
    .DESCRIPTION
        Clears the stored connection state (base URI and token).
    .EXAMPLE
        Disconnect-CfServer
    #>
    [CmdletBinding()]
    param()

    $script:CfConnection.BaseUri = $null
    $script:CfConnection.Token = $null
    $script:CfConnection.Headers = @{}

    Write-Verbose 'Disconnected from CarbonFiles server'
}
```

**Step 3: Commit**

```bash
git add clients/powershell/Public/Connect-CfServer.ps1 clients/powershell/Public/Disconnect-CfServer.ps1
git commit -m "feat: add Connect-CfServer and Disconnect-CfServer cmdlets"
```

---

### Task 7: PowerShell Module — Bucket Cmdlets

**Files:**
- Create: `clients/powershell/Public/New-CfBucket.ps1`
- Create: `clients/powershell/Public/Get-CfBucket.ps1`
- Create: `clients/powershell/Public/Update-CfBucket.ps1`
- Create: `clients/powershell/Public/Remove-CfBucket.ps1`
- Create: `clients/powershell/Public/Get-CfBucketSummary.ps1`
- Create: `clients/powershell/Public/Save-CfBucketZip.ps1`

**Step 1: Create New-CfBucket**

Create `clients/powershell/Public/New-CfBucket.ps1`:

```powershell
function New-CfBucket {
    <#
    .SYNOPSIS
        Creates a new bucket.
    .PARAMETER Name
        The name of the bucket.
    .PARAMETER Description
        Optional description.
    .PARAMETER ExpiresIn
        Optional expiry (e.g., "1h", "1d", "1w", "30d", or ISO 8601).
    .EXAMPLE
        New-CfBucket -Name "my-bucket" -ExpiresIn "7d"
    #>
    [CmdletBinding(SupportsShouldProcess)]
    [OutputType([PSCustomObject])]
    param(
        [Parameter(Mandatory, Position = 0)]
        [ValidateNotNullOrEmpty()]
        [string]$Name,

        [Parameter()]
        [string]$Description,

        [Parameter()]
        [string]$ExpiresIn
    )

    process {
        $body = @{ name = $Name }
        if ($Description) { $body.description = $Description }
        if ($ExpiresIn) { $body.expires_in = $ExpiresIn }

        if ($PSCmdlet.ShouldProcess($Name, 'Create bucket')) {
            Invoke-CfApiRequest -Method Post -Path '/api/buckets' -Body $body
        }
    }
}
```

**Step 2: Create Get-CfBucket**

Create `clients/powershell/Public/Get-CfBucket.ps1`:

```powershell
function Get-CfBucket {
    <#
    .SYNOPSIS
        Gets one or more buckets.
    .PARAMETER Id
        Get a specific bucket by ID. If omitted, lists all buckets.
    .PARAMETER Limit
        Maximum number of results (default 50).
    .PARAMETER Offset
        Number of results to skip.
    .PARAMETER IncludeExpired
        Include expired buckets in the list.
    .EXAMPLE
        Get-CfBucket
    .EXAMPLE
        Get-CfBucket -Id "abc1234567"
    #>
    [CmdletBinding(DefaultParameterSetName = 'List')]
    [OutputType([PSCustomObject])]
    param(
        [Parameter(Mandatory, Position = 0, ParameterSetName = 'ById',
                   ValueFromPipelineByPropertyName)]
        [string]$Id,

        [Parameter(ParameterSetName = 'List')]
        [int]$Limit = 50,

        [Parameter(ParameterSetName = 'List')]
        [int]$Offset = 0,

        [Parameter(ParameterSetName = 'List')]
        [switch]$IncludeExpired
    )

    process {
        if ($PSCmdlet.ParameterSetName -eq 'ById') {
            Invoke-CfApiRequest -Method Get -Path "/api/buckets/$Id"
        }
        else {
            $query = @{
                limit  = $Limit.ToString()
                offset = $Offset.ToString()
            }
            if ($IncludeExpired) { $query.include_expired = 'true' }
            $result = Invoke-CfApiRequest -Method Get -Path '/api/buckets' -Query $query
            if ($result.Items) { $result.Items } else { $result }
        }
    }
}
```

**Step 3: Create Update-CfBucket**

Create `clients/powershell/Public/Update-CfBucket.ps1`:

```powershell
function Update-CfBucket {
    <#
    .SYNOPSIS
        Updates a bucket's properties.
    .PARAMETER Id
        The bucket ID.
    .PARAMETER Name
        New name for the bucket.
    .PARAMETER Description
        New description for the bucket.
    .PARAMETER ExpiresIn
        New expiry duration or datetime.
    .EXAMPLE
        Update-CfBucket -Id "abc1234567" -Name "new-name"
    #>
    [CmdletBinding(SupportsShouldProcess)]
    [OutputType([PSCustomObject])]
    param(
        [Parameter(Mandatory, Position = 0, ValueFromPipelineByPropertyName)]
        [string]$Id,

        [Parameter()]
        [string]$Name,

        [Parameter()]
        [string]$Description,

        [Parameter()]
        [string]$ExpiresIn
    )

    process {
        $body = @{}
        if ($Name) { $body.name = $Name }
        if ($Description) { $body.description = $Description }
        if ($ExpiresIn) { $body.expires_in = $ExpiresIn }

        if ($body.Count -eq 0) {
            Write-Warning 'No properties specified to update.'
            return
        }

        if ($PSCmdlet.ShouldProcess($Id, 'Update bucket')) {
            Invoke-CfApiRequest -Method Patch -Path "/api/buckets/$Id" -Body $body
        }
    }
}
```

**Step 4: Create Remove-CfBucket**

Create `clients/powershell/Public/Remove-CfBucket.ps1`:

```powershell
function Remove-CfBucket {
    <#
    .SYNOPSIS
        Deletes a bucket and all its files.
    .PARAMETER Id
        The bucket ID to delete.
    .EXAMPLE
        Remove-CfBucket -Id "abc1234567"
    .EXAMPLE
        Get-CfBucket | Where-Object Name -like "temp*" | Remove-CfBucket
    #>
    [CmdletBinding(SupportsShouldProcess, ConfirmImpact = 'High')]
    param(
        [Parameter(Mandatory, Position = 0, ValueFromPipelineByPropertyName)]
        [string]$Id
    )

    process {
        if ($PSCmdlet.ShouldProcess($Id, 'Delete bucket')) {
            Invoke-CfApiRequest -Method Delete -Path "/api/buckets/$Id"
        }
    }
}
```

**Step 5: Create Get-CfBucketSummary**

Create `clients/powershell/Public/Get-CfBucketSummary.ps1`:

```powershell
function Get-CfBucketSummary {
    <#
    .SYNOPSIS
        Gets a text summary of a bucket.
    .PARAMETER Id
        The bucket ID.
    .EXAMPLE
        Get-CfBucketSummary -Id "abc1234567"
    #>
    [CmdletBinding()]
    [OutputType([string])]
    param(
        [Parameter(Mandatory, Position = 0, ValueFromPipelineByPropertyName)]
        [string]$Id
    )

    process {
        Invoke-CfApiRequest -Method Get -Path "/api/buckets/$Id/summary"
    }
}
```

**Step 6: Create Save-CfBucketZip**

Create `clients/powershell/Public/Save-CfBucketZip.ps1`:

```powershell
function Save-CfBucketZip {
    <#
    .SYNOPSIS
        Downloads a bucket's contents as a ZIP file.
    .PARAMETER Id
        The bucket ID.
    .PARAMETER OutPath
        Path to save the ZIP file. Defaults to "{Id}.zip" in current directory.
    .EXAMPLE
        Save-CfBucketZip -Id "abc1234567" -OutPath ./backup.zip
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory, Position = 0, ValueFromPipelineByPropertyName)]
        [string]$Id,

        [Parameter(Position = 1)]
        [string]$OutPath
    )

    process {
        if (-not $OutPath) { $OutPath = "$Id.zip" }
        Invoke-CfApiRequest -Method Get -Path "/api/buckets/$Id/zip" -OutFile $OutPath
        Get-Item $OutPath
    }
}
```

**Step 7: Commit**

```bash
git add clients/powershell/Public/New-CfBucket.ps1 clients/powershell/Public/Get-CfBucket.ps1 clients/powershell/Public/Update-CfBucket.ps1 clients/powershell/Public/Remove-CfBucket.ps1 clients/powershell/Public/Get-CfBucketSummary.ps1 clients/powershell/Public/Save-CfBucketZip.ps1
git commit -m "feat: add PowerShell bucket cmdlets"
```

---

### Task 8: PowerShell Module — File Cmdlets

**Files:**
- Create: `clients/powershell/Public/Get-CfFile.ps1`
- Create: `clients/powershell/Public/Remove-CfFile.ps1`
- Create: `clients/powershell/Public/Send-CfFile.ps1`
- Create: `clients/powershell/Public/Save-CfFileContent.ps1`

**Step 1: Create Get-CfFile**

Create `clients/powershell/Public/Get-CfFile.ps1`:

```powershell
function Get-CfFile {
    <#
    .SYNOPSIS
        Lists files in a bucket or gets a specific file's metadata.
    .PARAMETER BucketId
        The bucket ID.
    .PARAMETER Path
        Optional file path to get a specific file.
    .PARAMETER Limit
        Maximum number of results (default 50).
    .PARAMETER Offset
        Number of results to skip.
    .EXAMPLE
        Get-CfFile -BucketId "abc1234567"
    .EXAMPLE
        Get-CfFile -BucketId "abc1234567" -Path "docs/readme.txt"
    #>
    [CmdletBinding(DefaultParameterSetName = 'List')]
    [OutputType([PSCustomObject])]
    param(
        [Parameter(Mandatory, Position = 0, ValueFromPipelineByPropertyName)]
        [Alias('Id')]
        [string]$BucketId,

        [Parameter(Position = 1, ParameterSetName = 'ByPath')]
        [string]$Path,

        [Parameter(ParameterSetName = 'List')]
        [int]$Limit = 50,

        [Parameter(ParameterSetName = 'List')]
        [int]$Offset = 0
    )

    process {
        if ($Path) {
            $encodedPath = [uri]::EscapeDataString($Path)
            Invoke-CfApiRequest -Method Get -Path "/api/buckets/$BucketId/files/$encodedPath"
        }
        else {
            $query = @{
                limit  = $Limit.ToString()
                offset = $Offset.ToString()
            }
            $result = Invoke-CfApiRequest -Method Get -Path "/api/buckets/$BucketId/files" -Query $query
            if ($result.Items) { $result.Items } else { $result }
        }
    }
}
```

**Step 2: Create Remove-CfFile**

Create `clients/powershell/Public/Remove-CfFile.ps1`:

```powershell
function Remove-CfFile {
    <#
    .SYNOPSIS
        Deletes a file from a bucket.
    .PARAMETER BucketId
        The bucket ID.
    .PARAMETER Path
        The file path within the bucket.
    .EXAMPLE
        Remove-CfFile -BucketId "abc1234567" -Path "docs/readme.txt"
    #>
    [CmdletBinding(SupportsShouldProcess, ConfirmImpact = 'High')]
    param(
        [Parameter(Mandatory, Position = 0, ValueFromPipelineByPropertyName)]
        [Alias('Id')]
        [string]$BucketId,

        [Parameter(Mandatory, Position = 1, ValueFromPipelineByPropertyName)]
        [string]$Path
    )

    process {
        $encodedPath = [uri]::EscapeDataString($Path)
        if ($PSCmdlet.ShouldProcess("$BucketId/$Path", 'Delete file')) {
            Invoke-CfApiRequest -Method Delete -Path "/api/buckets/$BucketId/files/$encodedPath"
        }
    }
}
```

**Step 3: Create Send-CfFile**

Create `clients/powershell/Public/Send-CfFile.ps1`:

```powershell
function Send-CfFile {
    <#
    .SYNOPSIS
        Uploads a file to a bucket.
    .PARAMETER BucketId
        The bucket ID.
    .PARAMETER FilePath
        Path to the local file to upload.
    .PARAMETER DestinationPath
        Optional destination path in the bucket. Defaults to the filename.
    .PARAMETER Token
        Optional upload token (cfu_* prefix) instead of API key auth.
    .EXAMPLE
        Send-CfFile -BucketId "abc1234567" -FilePath ./report.pdf
    .EXAMPLE
        Send-CfFile -BucketId "abc1234567" -FilePath ./data.csv -DestinationPath "reports/data.csv"
    #>
    [CmdletBinding(SupportsShouldProcess)]
    [OutputType([PSCustomObject])]
    param(
        [Parameter(Mandatory, Position = 0, ValueFromPipelineByPropertyName)]
        [Alias('Id')]
        [string]$BucketId,

        [Parameter(Mandatory, Position = 1)]
        [ValidateScript({ Test-Path $_ -PathType Leaf })]
        [string]$FilePath,

        [Parameter()]
        [string]$DestinationPath,

        [Parameter()]
        [string]$Token
    )

    process {
        $file = Get-Item $FilePath
        $fileName = if ($DestinationPath) { $DestinationPath } else { $file.Name }

        if ($PSCmdlet.ShouldProcess("$fileName -> $BucketId", 'Upload file')) {
            $uri = "$($script:CfConnection.BaseUri)/api/buckets/$BucketId/upload/stream"
            $query = "filename=$([uri]::EscapeDataString($fileName))"
            if ($Token) { $query += "&token=$([uri]::EscapeDataString($Token))" }
            $uri += "?$query"

            $headers = $script:CfConnection.Headers.Clone()
            $headers['Content-Type'] = 'application/octet-stream'

            $fileStream = [System.IO.File]::OpenRead($file.FullName)
            try {
                $response = Invoke-RestMethod -Method Put -Uri $uri -Headers $headers -Body $fileStream
                if ($response) { ConvertTo-PascalCaseKeys $response }
            }
            finally {
                $fileStream.Dispose()
            }
        }
    }
}
```

**Step 4: Create Save-CfFileContent**

Create `clients/powershell/Public/Save-CfFileContent.ps1`:

```powershell
function Save-CfFileContent {
    <#
    .SYNOPSIS
        Downloads a file's content from a bucket.
    .PARAMETER BucketId
        The bucket ID.
    .PARAMETER Path
        The file path within the bucket.
    .PARAMETER OutPath
        Local path to save the file. Defaults to the filename in current directory.
    .EXAMPLE
        Save-CfFileContent -BucketId "abc1234567" -Path "report.pdf" -OutPath ./report.pdf
    .EXAMPLE
        Get-CfFile -BucketId "abc1234567" | Save-CfFileContent -OutPath ./downloads/
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory, Position = 0, ValueFromPipelineByPropertyName)]
        [Alias('Id')]
        [string]$BucketId,

        [Parameter(Mandatory, Position = 1, ValueFromPipelineByPropertyName)]
        [string]$Path,

        [Parameter(Position = 2)]
        [string]$OutPath
    )

    process {
        $fileName = [System.IO.Path]::GetFileName($Path)
        if (-not $OutPath) {
            $OutPath = $fileName
        }
        elseif (Test-Path $OutPath -PathType Container) {
            $OutPath = Join-Path $OutPath $fileName
        }

        $encodedPath = [uri]::EscapeDataString($Path)
        Invoke-CfApiRequest -Method Get -Path "/api/buckets/$BucketId/files/$encodedPath/content" -OutFile $OutPath
        Get-Item $OutPath
    }
}
```

**Step 5: Commit**

```bash
git add clients/powershell/Public/Get-CfFile.ps1 clients/powershell/Public/Remove-CfFile.ps1 clients/powershell/Public/Send-CfFile.ps1 clients/powershell/Public/Save-CfFileContent.ps1
git commit -m "feat: add PowerShell file cmdlets"
```

---

### Task 9: PowerShell Module — Key, Token, Stats, and Health Cmdlets

**Files:**
- Create: `clients/powershell/Public/New-CfApiKey.ps1`
- Create: `clients/powershell/Public/Get-CfApiKey.ps1`
- Create: `clients/powershell/Public/Remove-CfApiKey.ps1`
- Create: `clients/powershell/Public/Get-CfApiKeyUsage.ps1`
- Create: `clients/powershell/Public/New-CfUploadToken.ps1`
- Create: `clients/powershell/Public/New-CfDashboardToken.ps1`
- Create: `clients/powershell/Public/Test-CfDashboardToken.ps1`
- Create: `clients/powershell/Public/Get-CfStats.ps1`
- Create: `clients/powershell/Public/Test-CfHealth.ps1`

**Step 1: Create API key cmdlets**

Create `clients/powershell/Public/New-CfApiKey.ps1`:

```powershell
function New-CfApiKey {
    <#
    .SYNOPSIS
        Creates a new API key.
    .PARAMETER Name
        A descriptive name for the key.
    .EXAMPLE
        New-CfApiKey -Name "CI/CD Pipeline"
    #>
    [CmdletBinding(SupportsShouldProcess)]
    [OutputType([PSCustomObject])]
    param(
        [Parameter(Mandatory, Position = 0)]
        [ValidateNotNullOrEmpty()]
        [string]$Name
    )

    process {
        if ($PSCmdlet.ShouldProcess($Name, 'Create API key')) {
            Invoke-CfApiRequest -Method Post -Path '/api/keys' -Body @{ name = $Name }
        }
    }
}
```

Create `clients/powershell/Public/Get-CfApiKey.ps1`:

```powershell
function Get-CfApiKey {
    <#
    .SYNOPSIS
        Lists all API keys.
    .PARAMETER Limit
        Maximum number of results (default 50).
    .PARAMETER Offset
        Number of results to skip.
    .EXAMPLE
        Get-CfApiKey
    #>
    [CmdletBinding()]
    [OutputType([PSCustomObject])]
    param(
        [Parameter()]
        [int]$Limit = 50,

        [Parameter()]
        [int]$Offset = 0
    )

    process {
        $query = @{
            limit  = $Limit.ToString()
            offset = $Offset.ToString()
        }
        $result = Invoke-CfApiRequest -Method Get -Path '/api/keys' -Query $query
        if ($result.Items) { $result.Items } else { $result }
    }
}
```

Create `clients/powershell/Public/Remove-CfApiKey.ps1`:

```powershell
function Remove-CfApiKey {
    <#
    .SYNOPSIS
        Revokes an API key.
    .PARAMETER Prefix
        The key prefix (e.g., "cf4_abc123").
    .EXAMPLE
        Remove-CfApiKey -Prefix "cf4_abc123"
    #>
    [CmdletBinding(SupportsShouldProcess, ConfirmImpact = 'High')]
    param(
        [Parameter(Mandatory, Position = 0, ValueFromPipelineByPropertyName)]
        [string]$Prefix
    )

    process {
        if ($PSCmdlet.ShouldProcess($Prefix, 'Revoke API key')) {
            Invoke-CfApiRequest -Method Delete -Path "/api/keys/$Prefix"
        }
    }
}
```

Create `clients/powershell/Public/Get-CfApiKeyUsage.ps1`:

```powershell
function Get-CfApiKeyUsage {
    <#
    .SYNOPSIS
        Gets usage statistics for an API key.
    .PARAMETER Prefix
        The key prefix.
    .EXAMPLE
        Get-CfApiKeyUsage -Prefix "cf4_abc123"
    #>
    [CmdletBinding()]
    [OutputType([PSCustomObject])]
    param(
        [Parameter(Mandatory, Position = 0, ValueFromPipelineByPropertyName)]
        [string]$Prefix
    )

    process {
        Invoke-CfApiRequest -Method Get -Path "/api/keys/$Prefix/usage"
    }
}
```

**Step 2: Create token cmdlets**

Create `clients/powershell/Public/New-CfUploadToken.ps1`:

```powershell
function New-CfUploadToken {
    <#
    .SYNOPSIS
        Creates an upload token for a bucket.
    .PARAMETER BucketId
        The bucket ID.
    .PARAMETER ExpiresIn
        Optional expiry (e.g., "1h", "1d").
    .PARAMETER MaxUploads
        Optional maximum number of uploads allowed.
    .EXAMPLE
        New-CfUploadToken -BucketId "abc1234567" -ExpiresIn "24h" -MaxUploads 10
    #>
    [CmdletBinding(SupportsShouldProcess)]
    [OutputType([PSCustomObject])]
    param(
        [Parameter(Mandatory, Position = 0, ValueFromPipelineByPropertyName)]
        [Alias('Id')]
        [string]$BucketId,

        [Parameter()]
        [string]$ExpiresIn,

        [Parameter()]
        [int]$MaxUploads
    )

    process {
        $body = @{}
        if ($ExpiresIn) { $body.expires_in = $ExpiresIn }
        if ($MaxUploads -gt 0) { $body.max_uploads = $MaxUploads }

        if ($PSCmdlet.ShouldProcess($BucketId, 'Create upload token')) {
            Invoke-CfApiRequest -Method Post -Path "/api/buckets/$BucketId/tokens" -Body $body
        }
    }
}
```

Create `clients/powershell/Public/New-CfDashboardToken.ps1`:

```powershell
function New-CfDashboardToken {
    <#
    .SYNOPSIS
        Creates a dashboard authentication token.
    .PARAMETER ExpiresIn
        Optional expiry (default/max: 24h).
    .EXAMPLE
        New-CfDashboardToken -ExpiresIn "12h"
    #>
    [CmdletBinding(SupportsShouldProcess)]
    [OutputType([PSCustomObject])]
    param(
        [Parameter()]
        [string]$ExpiresIn
    )

    process {
        $body = @{}
        if ($ExpiresIn) { $body.expires_in = $ExpiresIn }

        if ($PSCmdlet.ShouldProcess('Dashboard', 'Create dashboard token')) {
            Invoke-CfApiRequest -Method Post -Path '/api/tokens/dashboard' -Body $body
        }
    }
}
```

Create `clients/powershell/Public/Test-CfDashboardToken.ps1`:

```powershell
function Test-CfDashboardToken {
    <#
    .SYNOPSIS
        Validates the current dashboard token.
    .EXAMPLE
        Test-CfDashboardToken
    #>
    [CmdletBinding()]
    [OutputType([PSCustomObject])]
    param()

    process {
        Invoke-CfApiRequest -Method Get -Path '/api/tokens/dashboard/me'
    }
}
```

**Step 3: Create stats and health cmdlets**

Create `clients/powershell/Public/Get-CfStats.ps1`:

```powershell
function Get-CfStats {
    <#
    .SYNOPSIS
        Gets system statistics (admin only).
    .EXAMPLE
        Get-CfStats
    #>
    [CmdletBinding()]
    [OutputType([PSCustomObject])]
    param()

    process {
        Invoke-CfApiRequest -Method Get -Path '/api/stats'
    }
}
```

Create `clients/powershell/Public/Test-CfHealth.ps1`:

```powershell
function Test-CfHealth {
    <#
    .SYNOPSIS
        Checks the server health status.
    .EXAMPLE
        Test-CfHealth
    #>
    [CmdletBinding()]
    [OutputType([PSCustomObject])]
    param()

    process {
        Invoke-CfApiRequest -Method Get -Path '/healthz'
    }
}
```

**Step 4: Commit**

```bash
git add clients/powershell/Public/
git commit -m "feat: add PowerShell key, token, stats, and health cmdlets"
```

---

### Task 10: CI/CD — Publish Clients Workflow

**Files:**
- Create: `.github/workflows/publish-clients.yml`

**Step 1: Create the workflow**

Create `.github/workflows/publish-clients.yml`:

```yaml
name: Publish Client SDKs

on:
  release:
    types: [published]

env:
  DOTNET_VERSION: '10.0.x'

jobs:
  export-spec:
    runs-on: ubuntu-latest
    outputs:
      version: ${{ steps.version.outputs.value }}
    steps:
      - uses: actions/checkout@v4

      - name: Setup .NET
        uses: actions/setup-dotnet@v4
        with:
          dotnet-version: ${{ env.DOTNET_VERSION }}

      - name: Extract version from tag
        id: version
        run: echo "value=${GITHUB_REF_NAME#v}" >> "$GITHUB_OUTPUT"

      - name: Build API
        run: dotnet build src/CarbonFiles.Api --configuration Release

      - name: Export OpenAPI spec
        run: |
          export CarbonFiles__DbPath="/tmp/cf-spec-$$.db"
          export CarbonFiles__DataDir="/tmp/cf-spec-$$"
          export CarbonFiles__AdminKey="spec-export"
          mkdir -p "$CarbonFiles__DataDir"

          dotnet run --project src/CarbonFiles.Api --configuration Release --no-build &
          API_PID=$!
          trap "kill $API_PID 2>/dev/null; rm -rf $CarbonFiles__DbPath $CarbonFiles__DataDir" EXIT

          for i in $(seq 1 30); do
            curl -sf http://localhost:5000/healthz > /dev/null 2>&1 && break
            sleep 1
          done

          curl -sf http://localhost:5000/openapi/v1.json -o openapi.json

      - name: Upload spec artifact
        uses: actions/upload-artifact@v4
        with:
          name: openapi-spec
          path: openapi.json

  publish-typescript:
    needs: export-spec
    runs-on: ubuntu-latest
    defaults:
      run:
        working-directory: clients/typescript
    steps:
      - uses: actions/checkout@v4

      - uses: actions/setup-node@v4
        with:
          node-version: '22'
          registry-url: 'https://registry.npmjs.org'

      - uses: actions/download-artifact@v4
        with:
          name: openapi-spec
          path: clients/typescript

      - name: Set version
        run: npm version "${{ needs.export-spec.outputs.version }}" --no-git-tag-version

      - run: npm ci

      - name: Generate client
        run: npm run generate

      - name: Build
        run: npm run build

      - name: Publish
        run: npm publish --access public
        env:
          NODE_AUTH_TOKEN: ${{ secrets.NPM_TOKEN }}

  publish-csharp:
    needs: export-spec
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4

      - name: Setup .NET
        uses: actions/setup-dotnet@v4
        with:
          dotnet-version: ${{ env.DOTNET_VERSION }}

      - uses: actions/download-artifact@v4
        with:
          name: openapi-spec
          path: clients/csharp

      - name: Build and pack
        run: >-
          dotnet pack clients/csharp/CarbonFiles.Client.csproj
          --configuration Release
          -p:PackageVersion=${{ needs.export-spec.outputs.version }}

      - name: Publish
        run: >-
          dotnet nuget push clients/csharp/bin/Release/*.nupkg
          --api-key ${{ secrets.NUGET_API_KEY }}
          --source https://api.nuget.org/v3/index.json

  publish-python:
    needs: export-spec
    runs-on: ubuntu-latest
    defaults:
      run:
        working-directory: clients/python
    steps:
      - uses: actions/checkout@v4

      - uses: actions/setup-python@v5
        with:
          python-version: '3.12'

      - uses: actions/download-artifact@v4
        with:
          name: openapi-spec
          path: clients/python

      - name: Install tools
        run: pip install openapi-python-client build twine

      - name: Update version in config
        run: |
          sed -i "s/package_version_override: .*/package_version_override: \"${{ needs.export-spec.outputs.version }}\"/" config.yml

      - name: Generate client
        run: |
          chmod +x generate.sh
          ./generate.sh

      - name: Build
        run: python -m build

      - name: Publish
        run: twine upload dist/*
        env:
          TWINE_USERNAME: __token__
          TWINE_PASSWORD: ${{ secrets.PYPI_API_TOKEN }}

  publish-powershell:
    needs: export-spec
    runs-on: ubuntu-latest
    defaults:
      run:
        shell: pwsh
        working-directory: clients/powershell
    steps:
      - uses: actions/checkout@v4

      - name: Update module version
        run: |
          $manifest = Get-Content CarbonFiles.psd1 -Raw
          $manifest = $manifest -replace "ModuleVersion\s*=\s*'[^']*'", "ModuleVersion = '${{ needs.export-spec.outputs.version }}'"
          Set-Content CarbonFiles.psd1 $manifest

      - name: Validate manifest
        run: Test-ModuleManifest CarbonFiles.psd1

      - name: Publish
        run: Publish-Module -Path . -NuGetApiKey $env:PSGALLERY_API_KEY -Verbose
        env:
          PSGALLERY_API_KEY: ${{ secrets.PSGALLERY_API_KEY }}
```

**Step 2: Validate the workflow syntax**

Run: `cat .github/workflows/publish-clients.yml | python3 -c "import yaml, sys; yaml.safe_load(sys.stdin); print('Valid YAML')"` (requires PyYAML)
Expected: "Valid YAML"

**Step 3: Commit**

```bash
git add .github/workflows/publish-clients.yml
git commit -m "ci: add client SDK publish workflow on GitHub Release"
```

---

### Task 11: Update Documentation

**Files:**
- Modify: `README.md`
- Modify: `CLAUDE.md`

**Step 1: Add SDK section to README.md**

Add a "Client SDKs" section to `README.md` after the "API Documentation" section, documenting install commands and quick-start usage for each language:

- TypeScript: `npm install @carbonfiles/client`
- C#: `dotnet add package CarbonFiles.Client`
- Python: `pip install carbonfiles-client`
- PowerShell: `Install-Module CarbonFiles`

Include a brief code example for each showing connection + basic operation.

**Step 2: Add SDK info to CLAUDE.md**

Add a "Client SDKs" section to `CLAUDE.md` documenting:
- The `clients/` directory structure
- Generator tools used per language
- How to regenerate clients locally (`scripts/export-openapi.sh` + per-language commands)
- The publish workflow trigger (GitHub Release)

**Step 3: Commit**

```bash
git add README.md CLAUDE.md
git commit -m "docs: add client SDK documentation to README and CLAUDE.md"
```

---

### Task 12: End-to-End Verification

**Step 1: Export the OpenAPI spec**

Run: `./scripts/export-openapi.sh openapi.json`
Expected: `openapi.json` exists and is valid JSON.

**Step 2: Generate and build TypeScript client**

Run:
```bash
cp openapi.json clients/typescript/openapi.json
cd clients/typescript && npm install && npm run generate && npm run build
```
Expected: `dist/` directory with compiled JS + type declarations.

**Step 3: Generate and build C# client**

Run:
```bash
cp openapi.json clients/csharp/openapi.json
dotnet build clients/csharp/CarbonFiles.Client.csproj --configuration Release
dotnet pack clients/csharp/CarbonFiles.Client.csproj --configuration Release
```
Expected: Build succeeds. `.nupkg` file in `bin/Release/`.

**Step 4: Generate and build Python client**

Run:
```bash
cp openapi.json clients/python/openapi.json
cd clients/python && chmod +x generate.sh && ./generate.sh && python -m build
```
Expected: `dist/` directory with `.tar.gz` and `.whl`.

**Step 5: Validate PowerShell module**

Run: `pwsh -Command "Test-ModuleManifest clients/powershell/CarbonFiles.psd1; Import-Module ./clients/powershell/CarbonFiles.psd1 -Force; Get-Command -Module CarbonFiles"`
Expected: All 21 cmdlets listed.

**Step 6: Commit any fixes from verification**

```bash
git add -A
git commit -m "fix: resolve issues found during end-to-end verification"
```
