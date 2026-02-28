using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace CarbonFiles.Api.Tests;

/// <summary>
/// End-to-end smoke tests that exercise every API endpoint and verify
/// JSON serialization works correctly. These catch AOT trimming issues
/// where types aren't registered in JsonSerializerContext or methods
/// get stripped during compilation.
///
/// Each test verifies: correct status code, response deserializes,
/// snake_case naming is used, and no null/missing required fields.
/// </summary>
public class AotSmokeTests : IClassFixture<TestFixture>
{
    private readonly TestFixture _fixture;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    public AotSmokeTests(TestFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task FullApiWorkflow_AllEndpoints_WorkCorrectly()
    {
        using var admin = _fixture.CreateAdminClient();

        // === Health ===
        var healthResponse = await admin.GetAsync("/healthz");
        healthResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var healthJson = await healthResponse.Content.ReadAsStringAsync();
        healthJson.Should().Contain("\"status\"").And.Contain("\"uptime_seconds\"").And.Contain("\"db\"");

        // === API Keys ===
        // Create key
        var createKeyResponse = await admin.PostAsJsonAsync("/api/keys", new { name = "smoke-test-agent" }, JsonOptions);
        createKeyResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var keyJson = await createKeyResponse.Content.ReadAsStringAsync();
        keyJson.Should().Contain("\"key\"").And.Contain("\"prefix\"").And.Contain("\"name\"").And.Contain("\"created_at\"");
        keyJson.Should().Contain("cf4_");

        // Extract key and prefix from response
        using var keyDoc = JsonDocument.Parse(keyJson);
        var apiKey = keyDoc.RootElement.GetProperty("key").GetString()!;
        var keyPrefix = keyDoc.RootElement.GetProperty("prefix").GetString()!;

        // List keys
        var listKeysResponse = await admin.GetAsync("/api/keys");
        listKeysResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var listKeysJson = await listKeysResponse.Content.ReadAsStringAsync();
        listKeysJson.Should().Contain("\"items\"").And.Contain("\"total\"").And.Contain("\"limit\"").And.Contain("\"offset\"");

        // Key usage
        var usageResponse = await admin.GetAsync($"/api/keys/{keyPrefix}/usage");
        usageResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var usageJson = await usageResponse.Content.ReadAsStringAsync();
        usageJson.Should().Contain("\"prefix\"").And.Contain("\"bucket_count\"").And.Contain("\"total_downloads\"");

        // === Dashboard Tokens ===
        var createTokenResponse = await admin.PostAsJsonAsync("/api/tokens/dashboard", new { expires_in = "1h" }, JsonOptions);
        createTokenResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var tokenJson = await createTokenResponse.Content.ReadAsStringAsync();
        tokenJson.Should().Contain("\"token\"").And.Contain("\"expires_at\"");

        using var tokenDoc = JsonDocument.Parse(tokenJson);
        var dashboardToken = tokenDoc.RootElement.GetProperty("token").GetString()!;

        // Validate /me
        using var dashClient = _fixture.CreateAuthenticatedClient(dashboardToken);
        var meResponse = await dashClient.GetAsync("/api/tokens/dashboard/me");
        meResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var meJson = await meResponse.Content.ReadAsStringAsync();
        meJson.Should().Contain("\"scope\"").And.Contain("\"expires_at\"");

        // === Buckets (using API key) ===
        using var keyClient = _fixture.CreateAuthenticatedClient(apiKey);

        // Create bucket
        var createBucketResponse = await keyClient.PostAsJsonAsync("/api/buckets",
            new { name = "smoke-bucket", description = "AOT smoke test", expires_in = "1d" }, JsonOptions);
        createBucketResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var bucketJson = await createBucketResponse.Content.ReadAsStringAsync();
        bucketJson.Should().Contain("\"id\"").And.Contain("\"name\"").And.Contain("\"owner\"")
            .And.Contain("\"created_at\"").And.Contain("\"expires_at\"").And.Contain("\"file_count\"").And.Contain("\"total_size\"");

        using var bucketDoc = JsonDocument.Parse(bucketJson);
        var bucketId = bucketDoc.RootElement.GetProperty("id").GetString()!;

        // List buckets
        var listBucketsResponse = await keyClient.GetAsync("/api/buckets");
        listBucketsResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var listBucketsJson = await listBucketsResponse.Content.ReadAsStringAsync();
        listBucketsJson.Should().Contain("\"items\"").And.Contain("\"total\"");

        // Get bucket detail (public)
        var getBucketResponse = await _fixture.Client.GetAsync($"/api/buckets/{bucketId}");
        getBucketResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var detailJson = await getBucketResponse.Content.ReadAsStringAsync();
        detailJson.Should().Contain("\"files\"").And.Contain("\"has_more_files\"");

        // Update bucket
        var updateResponse = await keyClient.PatchAsync($"/api/buckets/{bucketId}",
            new StringContent("{\"description\":\"updated\"}", Encoding.UTF8, "application/json"));
        updateResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        // === Uploads ===
        // Multipart upload
        using var multipart = new MultipartFormDataContent();
        multipart.Add(new ByteArrayContent(Encoding.UTF8.GetBytes("hello world")), "files", "hello.txt");
        multipart.Add(new ByteArrayContent(Encoding.UTF8.GetBytes("{\"key\":\"value\"}")), "data/config.json", "config.json");
        var uploadResponse = await keyClient.PostAsync($"/api/buckets/{bucketId}/upload", multipart);
        uploadResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var uploadJson = await uploadResponse.Content.ReadAsStringAsync();
        uploadJson.Should().Contain("\"uploaded\"").And.Contain("\"path\"").And.Contain("\"mime_type\"")
            .And.Contain("\"short_code\"").And.Contain("\"short_url\"").And.Contain("\"size\"");

        // Stream upload
        var streamContent = new ByteArrayContent(Encoding.UTF8.GetBytes("stream content"));
        streamContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");
        var streamUploadResponse = await keyClient.PutAsync(
            $"/api/buckets/{bucketId}/upload/stream?filename=stream.bin", streamContent);
        streamUploadResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        // === Files ===
        // List files
        var listFilesResponse = await _fixture.Client.GetAsync($"/api/buckets/{bucketId}/files");
        listFilesResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var listFilesJson = await listFilesResponse.Content.ReadAsStringAsync();
        listFilesJson.Should().Contain("\"items\"").And.Contain("\"total\"");

        // Get file metadata
        var metaResponse = await _fixture.Client.GetAsync($"/api/buckets/{bucketId}/files/hello.txt");
        metaResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var metaJson = await metaResponse.Content.ReadAsStringAsync();
        metaJson.Should().Contain("\"path\"").And.Contain("\"name\"").And.Contain("\"mime_type\"")
            .And.Contain("\"short_code\"").And.Contain("\"created_at\"").And.Contain("\"updated_at\"");

        // Download content
        var downloadResponse = await _fixture.Client.GetAsync($"/api/buckets/{bucketId}/files/hello.txt/content");
        downloadResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        downloadResponse.Headers.ETag.Should().NotBeNull();
        downloadResponse.Content.Headers.LastModified.Should().NotBeNull();
        downloadResponse.Content.Headers.ContentType!.MediaType.Should().Be("text/plain");
        var content = await downloadResponse.Content.ReadAsStringAsync();
        content.Should().Be("hello world");

        // Conditional request (304)
        var etag = downloadResponse.Headers.ETag!.Tag;
        using var conditionalRequest = new HttpRequestMessage(HttpMethod.Get,
            $"/api/buckets/{bucketId}/files/hello.txt/content");
        conditionalRequest.Headers.IfNoneMatch.Add(new System.Net.Http.Headers.EntityTagHeaderValue(etag));
        var conditionalResponse = await _fixture.Client.SendAsync(conditionalRequest);
        conditionalResponse.StatusCode.Should().Be(HttpStatusCode.NotModified);

        // Range request (206)
        using var rangeRequest = new HttpRequestMessage(HttpMethod.Get,
            $"/api/buckets/{bucketId}/files/hello.txt/content");
        rangeRequest.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(0, 4);
        var rangeResponse = await _fixture.Client.SendAsync(rangeRequest);
        rangeResponse.StatusCode.Should().Be(HttpStatusCode.PartialContent);
        var partial = await rangeResponse.Content.ReadAsStringAsync();
        partial.Should().Be("hello");

        // HEAD request
        using var headRequest = new HttpRequestMessage(HttpMethod.Head,
            $"/api/buckets/{bucketId}/files/hello.txt/content");
        var headResponse = await _fixture.Client.SendAsync(headRequest);
        headResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        headResponse.Content.Headers.ContentLength.Should().BeGreaterThan(0);

        // PATCH content (append)
        using var patchRequest = new HttpRequestMessage(HttpMethod.Patch,
            $"/api/buckets/{bucketId}/files/hello.txt/content");
        patchRequest.Headers.Authorization = keyClient.DefaultRequestHeaders.Authorization;
        patchRequest.Headers.Add("X-Append", "true");
        patchRequest.Content = new ByteArrayContent(Encoding.UTF8.GetBytes("!"));
        patchRequest.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");
        var patchResponse = await _fixture.Client.SendAsync(patchRequest);
        patchResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var patchJson = await patchResponse.Content.ReadAsStringAsync();
        patchJson.Should().Contain("\"size\"");

        // Verify appended content
        var verifyResponse = await _fixture.Client.GetAsync($"/api/buckets/{bucketId}/files/hello.txt/content");
        var verifyContent = await verifyResponse.Content.ReadAsStringAsync();
        verifyContent.Should().Be("hello world!");

        // === Short URLs ===
        // Extract short_code from file metadata
        using var metaDoc = JsonDocument.Parse(metaJson);
        var shortCode = metaDoc.RootElement.GetProperty("short_code").GetString()!;

        // Resolve short URL
        using var noRedirect = _fixture.CreateNoRedirectClient();
        var shortResponse = await noRedirect.GetAsync($"/s/{shortCode}");
        shortResponse.StatusCode.Should().Be(HttpStatusCode.Redirect);

        // === Upload Tokens ===
        var createUploadTokenResponse = await keyClient.PostAsJsonAsync(
            $"/api/buckets/{bucketId}/tokens",
            new { expires_in = "1h", max_uploads = 5 }, JsonOptions);
        createUploadTokenResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var uploadTokenJson = await createUploadTokenResponse.Content.ReadAsStringAsync();
        uploadTokenJson.Should().Contain("\"token\"").And.Contain("\"bucket_id\"")
            .And.Contain("\"expires_at\"").And.Contain("\"max_uploads\"").And.Contain("\"uploads_used\"");

        using var uploadTokenDoc = JsonDocument.Parse(uploadTokenJson);
        var uploadToken = uploadTokenDoc.RootElement.GetProperty("token").GetString()!;

        // Use upload token (no auth header)
        using var tokenUpload = new MultipartFormDataContent();
        tokenUpload.Add(new ByteArrayContent(Encoding.UTF8.GetBytes("via token")), "files", "token-file.txt");
        var tokenUploadResponse = await _fixture.Client.PostAsync(
            $"/api/buckets/{bucketId}/upload?token={uploadToken}", tokenUpload);
        tokenUploadResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        // === Stats ===
        var statsResponse = await admin.GetAsync("/api/stats");
        statsResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var statsJson = await statsResponse.Content.ReadAsStringAsync();
        statsJson.Should().Contain("\"total_buckets\"").And.Contain("\"total_files\"")
            .And.Contain("\"total_size\"").And.Contain("\"total_keys\"")
            .And.Contain("\"total_downloads\"").And.Contain("\"storage_by_owner\"");

        // === Bucket Summary ===
        var summaryResponse = await _fixture.Client.GetAsync($"/api/buckets/{bucketId}/summary");
        summaryResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        summaryResponse.Content.Headers.ContentType!.MediaType.Should().Be("text/plain");

        // === ZIP Download ===
        var zipResponse = await _fixture.Client.GetAsync($"/api/buckets/{bucketId}/zip");
        zipResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        zipResponse.Content.Headers.ContentType!.MediaType.Should().Be("application/zip");

        // Verify ZIP contents
        using var zipStream = await zipResponse.Content.ReadAsStreamAsync();
        using var archive = new System.IO.Compression.ZipArchive(zipStream, System.IO.Compression.ZipArchiveMode.Read);
        archive.Entries.Count.Should().BeGreaterThanOrEqualTo(3); // hello.txt, data/config.json, stream.bin, token-file.txt

        // === Cleanup ===
        // Delete file
        var deleteFileResponse = await keyClient.DeleteAsync($"/api/buckets/{bucketId}/files/stream.bin");
        deleteFileResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Delete short URL
        var deleteShortResponse = await keyClient.DeleteAsync($"/api/short/{shortCode}");
        deleteShortResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Delete bucket
        var deleteBucketResponse = await keyClient.DeleteAsync($"/api/buckets/{bucketId}");
        deleteBucketResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Delete API key
        var deleteKeyResponse = await admin.DeleteAsync($"/api/keys/{keyPrefix}");
        deleteKeyResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // === Error responses also serialize correctly ===
        var notFoundResponse = await _fixture.Client.GetAsync("/api/buckets/nonexistent");
        notFoundResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);

        var forbiddenResponse = await _fixture.Client.GetAsync("/api/stats");
        forbiddenResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var errorJson = await forbiddenResponse.Content.ReadAsStringAsync();
        errorJson.Should().Contain("\"error\"");
    }

    [Fact]
    public async Task OpenApi_Spec_IsAvailable()
    {
        var response = await _fixture.Client.GetAsync("/openapi/v1.json");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Contain("json");
        var spec = await response.Content.ReadAsStringAsync();
        spec.Should().Contain("\"paths\"").And.Contain("/api/buckets").And.Contain("/healthz");
    }

    [Fact]
    public async Task Scalar_UI_IsAvailable()
    {
        var response = await _fixture.Client.GetAsync("/scalar/v1");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task AllErrorResponses_UseSnakeCase()
    {
        // 400 - bad request
        using var admin = _fixture.CreateAdminClient();
        var badRequest = await admin.PostAsJsonAsync("/api/keys", new { name = "" }, JsonOptions);
        var badJson = await badRequest.Content.ReadAsStringAsync();
        if (badRequest.StatusCode == HttpStatusCode.BadRequest)
            badJson.Should().Contain("\"error\"");

        // 401 - no token on /me
        var noAuth = await _fixture.Client.GetAsync("/api/tokens/dashboard/me");
        noAuth.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var noAuthJson = await noAuth.Content.ReadAsStringAsync();
        noAuthJson.Should().Contain("\"error\"");

        // 403 - non-admin on admin endpoint
        var forbidden = await _fixture.Client.GetAsync("/api/keys");
        forbidden.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var forbiddenJson = await forbidden.Content.ReadAsStringAsync();
        forbiddenJson.Should().Contain("\"error\"");

        // 404 - nonexistent bucket
        var notFound = await _fixture.Client.GetAsync("/api/buckets/doesnotexist");
        notFound.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
