using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace CarbonFiles.Api.Tests.Endpoints;

public class PatchContentTests : IClassFixture<TestFixture>
{
    private readonly TestFixture _fixture;

    public PatchContentTests(TestFixture fixture) => _fixture = fixture;

    // -- Helpers ──────────────────────────────────────────────────────────

    private async Task<string> CreateBucketAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync("/api/buckets", new { name = $"patch-test-{Guid.NewGuid():N}" }, TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var json = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.GetProperty("id").GetString()!;
    }

    private async Task UploadFileAsync(HttpClient client, string bucketId, string fileName, byte[] content)
    {
        var multipart = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(content);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        multipart.Add(fileContent, "file", fileName);

        var response = await client.PostAsync($"/api/buckets/{bucketId}/upload", multipart, TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    private async Task<byte[]> DownloadFileAsync(HttpClient client, string bucketId, string fileName)
    {
        var response = await client.GetAsync($"/api/buckets/{bucketId}/files/{fileName}/content", TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return await response.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken);
    }

    private static async Task<JsonElement> ParseJsonAsync(HttpResponseMessage response)
    {
        var json = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        return JsonDocument.Parse(json).RootElement;
    }

    private static HttpRequestMessage CreatePatchRequest(string url, byte[] body, string? contentRange = null, bool append = false)
    {
        var request = new HttpRequestMessage(new HttpMethod("PATCH"), url);
        request.Content = new ByteArrayContent(body);
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");

        if (contentRange != null)
            request.Content.Headers.TryAddWithoutValidation("Content-Range", contentRange);

        if (append)
            request.Headers.Add("X-Append", "true");

        return request;
    }

    // -- Overwrite byte range at specific offset ─────────────────────────

    [Fact]
    public async Task Patch_OverwriteRange_UpdatesContent()
    {
        using var client = _fixture.CreateAdminClient();
        var bucketId = await CreateBucketAsync(client);

        // Upload: "Hello, World!"
        var original = Encoding.UTF8.GetBytes("Hello, World!");
        await UploadFileAsync(client, bucketId, "patch-test.txt", original);

        // PATCH bytes 7-11 with "Earth" (overwriting "World")
        var patchBody = Encoding.UTF8.GetBytes("Earth");
        var request = CreatePatchRequest(
            $"/api/buckets/{bucketId}/files/patch-test.txt/content",
            patchBody,
            contentRange: "bytes 7-11/*");

        var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        // Download and verify
        var downloaded = await DownloadFileAsync(_fixture.Client, bucketId, "patch-test.txt");
        Encoding.UTF8.GetString(downloaded).Should().Be("Hello, Earth!");
    }

    // -- Append to file ──────────────────────────────────────────────────

    [Fact]
    public async Task Patch_AppendToFile_ExtendsContent()
    {
        using var client = _fixture.CreateAdminClient();
        var bucketId = await CreateBucketAsync(client);

        // Upload: "Hello"
        var original = Encoding.UTF8.GetBytes("Hello");
        await UploadFileAsync(client, bucketId, "append-test.txt", original);

        // PATCH with append: ", World!"
        var appendBody = Encoding.UTF8.GetBytes(", World!");
        var request = CreatePatchRequest(
            $"/api/buckets/{bucketId}/files/append-test.txt/content",
            appendBody,
            append: true);

        var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        // Download and verify
        var downloaded = await DownloadFileAsync(_fixture.Client, bucketId, "append-test.txt");
        Encoding.UTF8.GetString(downloaded).Should().Be("Hello, World!");
    }

    // -- PATCH nonexistent file returns 404 ──────────────────────────────

    [Fact]
    public async Task Patch_NonexistentFile_Returns404()
    {
        using var client = _fixture.CreateAdminClient();
        var bucketId = await CreateBucketAsync(client);

        var patchBody = Encoding.UTF8.GetBytes("data");
        var request = CreatePatchRequest(
            $"/api/buckets/{bucketId}/files/nonexistent.txt/content",
            patchBody,
            contentRange: "bytes 0-3/*");

        var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);

        var body = await ParseJsonAsync(response);
        body.GetProperty("error").GetString().Should().Contain("File not found");
    }

    // -- PATCH without auth returns 403 ──────────────────────────────────

    [Fact]
    public async Task Patch_WithoutAuth_Returns403()
    {
        using var admin = _fixture.CreateAdminClient();
        var bucketId = await CreateBucketAsync(admin);

        await UploadFileAsync(admin, bucketId, "auth-test.txt", Encoding.UTF8.GetBytes("original"));

        var patchBody = Encoding.UTF8.GetBytes("patched");
        var request = CreatePatchRequest(
            $"/api/buckets/{bucketId}/files/auth-test.txt/content",
            patchBody,
            contentRange: "bytes 0-6/*");

        var response = await _fixture.Client.SendAsync(request, TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // -- Invalid Content-Range returns 400 ───────────────────────────────

    [Fact]
    public async Task Patch_MissingContentRange_WithoutAppend_Returns400()
    {
        using var client = _fixture.CreateAdminClient();
        var bucketId = await CreateBucketAsync(client);

        await UploadFileAsync(client, bucketId, "no-range.txt", Encoding.UTF8.GetBytes("original"));

        // PATCH without Content-Range and without X-Append
        var patchBody = Encoding.UTF8.GetBytes("data");
        var request = CreatePatchRequest(
            $"/api/buckets/{bucketId}/files/no-range.txt/content",
            patchBody);

        var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var body = await ParseJsonAsync(response);
        body.GetProperty("error").GetString().Should().Contain("Content-Range");
    }

    [Fact]
    public async Task Patch_InvalidContentRange_Returns400()
    {
        using var client = _fixture.CreateAdminClient();
        var bucketId = await CreateBucketAsync(client);

        await UploadFileAsync(client, bucketId, "bad-range.txt", Encoding.UTF8.GetBytes("original"));

        var patchBody = Encoding.UTF8.GetBytes("data");
        var request = CreatePatchRequest(
            $"/api/buckets/{bucketId}/files/bad-range.txt/content",
            patchBody,
            contentRange: "bytes gibberish");

        var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // -- Range not satisfiable returns 416 ───────────────────────────────

    [Fact]
    public async Task Patch_OffsetBeyondFileSize_Returns416()
    {
        using var client = _fixture.CreateAdminClient();
        var bucketId = await CreateBucketAsync(client);

        // Upload a 5-byte file
        await UploadFileAsync(client, bucketId, "small.txt", Encoding.UTF8.GetBytes("small"));

        // Try to patch at offset 100 (beyond file size of 5)
        var patchBody = Encoding.UTF8.GetBytes("data");
        var request = CreatePatchRequest(
            $"/api/buckets/{bucketId}/files/small.txt/content",
            patchBody,
            contentRange: "bytes 100-103/*");

        var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.RequestedRangeNotSatisfiable);
    }

    // -- File size updated after PATCH ────────────────────────────────────

    [Fact]
    public async Task Patch_Append_UpdatesFileSize()
    {
        using var client = _fixture.CreateAdminClient();
        var bucketId = await CreateBucketAsync(client);

        // Upload 5 bytes
        await UploadFileAsync(client, bucketId, "size-test.txt", Encoding.UTF8.GetBytes("Hello"));

        // Append 8 bytes
        var appendBody = Encoding.UTF8.GetBytes(", World!");
        var request = CreatePatchRequest(
            $"/api/buckets/{bucketId}/files/size-test.txt/content",
            appendBody,
            append: true);

        var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        // Check metadata shows updated size
        var body = await ParseJsonAsync(response);
        body.GetProperty("size").GetInt64().Should().Be(13); // 5 + 8
    }

    [Fact]
    public async Task Patch_Overwrite_DoesNotChangeSize_WhenWithinBounds()
    {
        using var client = _fixture.CreateAdminClient();
        var bucketId = await CreateBucketAsync(client);

        // Upload: "AAAAAAAAAA" (10 bytes)
        await UploadFileAsync(client, bucketId, "overwrite-size.txt", Encoding.UTF8.GetBytes("AAAAAAAAAA"));

        // Overwrite bytes 2-4 with "BBB" (still 10 bytes total)
        var patchBody = Encoding.UTF8.GetBytes("BBB");
        var request = CreatePatchRequest(
            $"/api/buckets/{bucketId}/files/overwrite-size.txt/content",
            patchBody,
            contentRange: "bytes 2-4/*");

        var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await ParseJsonAsync(response);
        body.GetProperty("size").GetInt64().Should().Be(10);
    }

    // -- Verify patched content is correct ────────────────────────────────

    [Fact]
    public async Task Patch_OverwriteMiddle_ContentIsCorrect()
    {
        using var client = _fixture.CreateAdminClient();
        var bucketId = await CreateBucketAsync(client);

        // Upload: "0123456789" (10 bytes)
        var original = Encoding.UTF8.GetBytes("0123456789");
        await UploadFileAsync(client, bucketId, "verify-content.txt", original);

        // Overwrite bytes 3-5 with "XYZ"
        var patchBody = Encoding.UTF8.GetBytes("XYZ");
        var request = CreatePatchRequest(
            $"/api/buckets/{bucketId}/files/verify-content.txt/content",
            patchBody,
            contentRange: "bytes 3-5/*");

        var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        // Download and verify full content
        var downloaded = await DownloadFileAsync(_fixture.Client, bucketId, "verify-content.txt");
        Encoding.UTF8.GetString(downloaded).Should().Be("012XYZ6789");
    }

    [Fact]
    public async Task Patch_OverwriteAtStart_ContentIsCorrect()
    {
        using var client = _fixture.CreateAdminClient();
        var bucketId = await CreateBucketAsync(client);

        // Upload: "ABCDEFGHIJ" (10 bytes)
        await UploadFileAsync(client, bucketId, "start-patch.txt", Encoding.UTF8.GetBytes("ABCDEFGHIJ"));

        // Overwrite bytes 0-2 with "XYZ"
        var patchBody = Encoding.UTF8.GetBytes("XYZ");
        var request = CreatePatchRequest(
            $"/api/buckets/{bucketId}/files/start-patch.txt/content",
            patchBody,
            contentRange: "bytes 0-2/*");

        var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var downloaded = await DownloadFileAsync(_fixture.Client, bucketId, "start-patch.txt");
        Encoding.UTF8.GetString(downloaded).Should().Be("XYZDEFGHIJ");
    }

    // -- Append with Content-Range: bytes */* ────────────────────────────

    [Fact]
    public async Task Patch_AppendWithContentRangeStarStar_Works()
    {
        using var client = _fixture.CreateAdminClient();
        var bucketId = await CreateBucketAsync(client);

        await UploadFileAsync(client, bucketId, "star-append.txt", Encoding.UTF8.GetBytes("Start"));

        // Use X-Append: true with Content-Range: bytes */*
        var appendBody = Encoding.UTF8.GetBytes("End");
        var request = CreatePatchRequest(
            $"/api/buckets/{bucketId}/files/star-append.txt/content",
            appendBody,
            contentRange: "bytes */*",
            append: true);

        var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var downloaded = await DownloadFileAsync(_fixture.Client, bucketId, "star-append.txt");
        Encoding.UTF8.GetString(downloaded).Should().Be("StartEnd");
    }

    // -- Bucket total size updated after PATCH ───────────────────────────

    [Fact]
    public async Task Patch_Append_UpdatesBucketTotalSize()
    {
        using var client = _fixture.CreateAdminClient();
        var bucketId = await CreateBucketAsync(client);

        // Upload 5 bytes
        await UploadFileAsync(client, bucketId, "bucket-size.txt", Encoding.UTF8.GetBytes("Hello"));

        // Check initial bucket stats
        var bucketResp1 = await _fixture.Client.GetAsync($"/api/buckets/{bucketId}", TestContext.Current.CancellationToken);
        var bucket1 = await ParseJsonAsync(bucketResp1);
        var initialSize = bucket1.GetProperty("total_size").GetInt64();
        initialSize.Should().Be(5);

        // Append 8 bytes
        var appendBody = Encoding.UTF8.GetBytes(", World!");
        var request = CreatePatchRequest(
            $"/api/buckets/{bucketId}/files/bucket-size.txt/content",
            appendBody,
            append: true);

        var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        // Check bucket stats updated
        var bucketResp2 = await _fixture.Client.GetAsync($"/api/buckets/{bucketId}", TestContext.Current.CancellationToken);
        var bucket2 = await ParseJsonAsync(bucketResp2);
        bucket2.GetProperty("total_size").GetInt64().Should().Be(13); // 5 + 8
    }

    // -- PATCH returns updated metadata ──────────────────────────────────

    [Fact]
    public async Task Patch_ReturnsUpdatedMetadata()
    {
        using var client = _fixture.CreateAdminClient();
        var bucketId = await CreateBucketAsync(client);

        await UploadFileAsync(client, bucketId, "meta-test.txt", Encoding.UTF8.GetBytes("original"));

        var patchBody = Encoding.UTF8.GetBytes(" content appended");
        var request = CreatePatchRequest(
            $"/api/buckets/{bucketId}/files/meta-test.txt/content",
            patchBody,
            append: true);

        var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await ParseJsonAsync(response);
        body.GetProperty("path").GetString().Should().Be("meta-test.txt");
        body.GetProperty("name").GetString().Should().Be("meta-test.txt");
        body.GetProperty("size").GetInt64().Should().Be(Encoding.UTF8.GetByteCount("original") + Encoding.UTF8.GetByteCount(" content appended"));
        body.TryGetProperty("mime_type", out _).Should().BeTrue();
    }

    // -- PATCH nonexistent bucket returns 404 ────────────────────────────

    [Fact]
    public async Task Patch_NonexistentBucket_Returns404()
    {
        using var client = _fixture.CreateAdminClient();

        var patchBody = Encoding.UTF8.GetBytes("data");
        var request = CreatePatchRequest(
            "/api/buckets/nonexistent/files/test.txt/content",
            patchBody,
            contentRange: "bytes 0-3/*");

        var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // -- PATCH path not ending with /content returns 404 ─────────────────

    [Fact]
    public async Task Patch_PathWithoutContent_Returns404()
    {
        using var client = _fixture.CreateAdminClient();
        var bucketId = await CreateBucketAsync(client);

        await UploadFileAsync(client, bucketId, "no-content-suffix.txt", Encoding.UTF8.GetBytes("data"));

        var patchBody = Encoding.UTF8.GetBytes("patch");
        var request = new HttpRequestMessage(new HttpMethod("PATCH"),
            $"/api/buckets/{bucketId}/files/no-content-suffix.txt");
        request.Content = new ByteArrayContent(patchBody);
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        request.Content.Headers.Add("Content-Range", "bytes 0-4/*");

        var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
