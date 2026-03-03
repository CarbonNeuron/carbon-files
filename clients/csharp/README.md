# CarbonFiles.Client

C# client for the [CarbonFiles](https://github.com/CarbonNeuron/carbon-files) file-sharing API. Generated from the OpenAPI spec using [Refitter](https://github.com/christianhelle/refitter) and [Refit](https://github.com/reactiveui/refit).

## Installation

```bash
dotnet add package CarbonFiles.Client
```

Targets `net10.0` and `netstandard2.0`.

## Quick Start

### With dependency injection

```csharp
using CarbonFiles.Client;

// Register in DI container
services.AddCarbonFilesClient(new Uri("https://files.example.com"))
    .ConfigureHttpClient(c =>
    {
        c.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", "cf4_your_api_key");
    });
```

Then inject and use `ICarbonFilesApi`:

```csharp
public class MyService(ICarbonFilesApi api)
{
    public async Task CreateBucketAsync()
    {
        var bucket = await api.BucketsPOST(new CreateBucketRequest
        {
            Name = "my-bucket",
            Description = "Project assets",
            Expires_in = "30d"
        });

        Console.WriteLine($"Created bucket: {bucket.Id}");
    }
}
```

### Without dependency injection

```csharp
using CarbonFiles.Client;
using Refit;

var client = new HttpClient
{
    BaseAddress = new Uri("https://files.example.com")
};
client.DefaultRequestHeaders.Authorization =
    new AuthenticationHeaderValue("Bearer", "cf4_your_api_key");

var api = RestService.For<ICarbonFilesApi>(client, new RefitSettings
{
    ContentSerializer = new SystemTextJsonContentSerializer(new JsonSerializerOptions
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    })
});

var bucket = await api.BucketsPOST(new CreateBucketRequest { Name = "my-bucket" });
```

## Common Operations

### Create a bucket

```csharp
var bucket = await api.BucketsPOST(new CreateBucketRequest
{
    Name = "my-bucket",
    Description = "Shared files",
    Expires_in = "7d"
});
```

### Upload a file

```csharp
var result = await api.Upload("bucket-id");
```

### List files in a bucket

```csharp
var files = await api.FilesGET("bucket-id", limit: 50, offset: 0);
foreach (var file in files.Items)
{
    Console.WriteLine($"{file.Name} ({file.Size} bytes)");
}
```

### Get bucket details

```csharp
var detail = await api.BucketsGET2("bucket-id");
Console.WriteLine($"Bucket: {detail.Name}, Files: {detail.Files.Count}");
```

### Delete a bucket

```csharp
await api.BucketsDELETE("bucket-id");
```

### Download bucket as ZIP

```csharp
var zipStream = await api.ZipGET("bucket-id");
```

## Authentication

CarbonFiles supports four token types, all passed as Bearer tokens:

| Type | Format | Scope |
|------|--------|-------|
| Admin key | Any string | Full access |
| API key | `cf4_` prefix | Own buckets |
| Dashboard JWT | JWT token | Admin-level, 24h max |
| Upload token | `cfu_` prefix | Single bucket |

Set the token on the HttpClient:

```csharp
services.AddCarbonFilesClient(new Uri("https://files.example.com"))
    .ConfigureHttpClient(c =>
    {
        c.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", "cf4_your_api_key");
    });
```

To switch tokens at runtime, use a `DelegatingHandler`:

```csharp
public class AuthHandler(ITokenProvider tokens) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        request.Headers.Authorization =
            new AuthenticationHeaderValue("Bearer", await tokens.GetTokenAsync());
        return await base.SendAsync(request, cancellationToken);
    }
}

services.AddCarbonFilesClient(new Uri("https://files.example.com"))
    .AddHttpMessageHandler<AuthHandler>();
```

## API Methods

| Method | Description |
|--------|-------------|
| `Healthz()` | Health check |
| `BucketsPOST(body)` | Create bucket |
| `BucketsGET(...)` | List buckets |
| `BucketsGET2(id)` | Get bucket details |
| `BucketsPATCH(id, body)` | Update bucket |
| `BucketsDELETE(id)` | Delete bucket |
| `Summary(id)` | Bucket summary (plaintext) |
| `ZipGET(id)` | Download bucket as ZIP |
| `FilesGET(id, ...)` | List files |
| `FilesGET2(id, filePath)` | Get file |
| `FilesDELETE(id, filePath)` | Delete file |
| `FilesPATCH(id, filePath)` | Patch file |
| `Upload(id)` | Upload files (multipart) |
| `Stream(id)` | Stream upload |
| `S(code)` | Resolve short URL |
| `Short(code)` | Delete short URL |
| `KeysPOST(body)` | Create API key |
| `KeysGET(...)` | List API keys |
| `KeysDELETE(prefix)` | Revoke API key |
| `Usage(prefix)` | API key usage stats |
| `Tokens(id, body)` | Create upload token |
| `Dashboard(body)` | Create dashboard token |
| `Me()` | Validate dashboard token |
| `Stats()` | System statistics |

## Links

- [CarbonFiles repository](https://github.com/CarbonNeuron/carbon-files)
- [NuGet package](https://www.nuget.org/packages/CarbonFiles.Client)
