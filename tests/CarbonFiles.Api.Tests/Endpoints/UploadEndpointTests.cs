using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace CarbonFiles.Api.Tests.Endpoints;

public class UploadEndpointTests : IClassFixture<TestFixture>
{
    private readonly TestFixture _fixture;

    public UploadEndpointTests(TestFixture fixture) => _fixture = fixture;

    // ── Helpers ──────────────────────────────────────────────────────────

    private async Task<string> CreateBucketAsync(HttpClient? client = null)
    {
        var c = client ?? _fixture.CreateAdminClient();
        var response = await c.PostAsJsonAsync("/api/buckets", new { name = $"upload-test-{Guid.NewGuid():N}" }, TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var json = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.GetProperty("id").GetString()!;
    }

    private static MultipartFormDataContent CreateMultipartContent(string fieldName, string fileName, string content)
    {
        var multipart = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(Encoding.UTF8.GetBytes(content));
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        multipart.Add(fileContent, fieldName, fileName);
        return multipart;
    }

    private static async Task<JsonElement> ParseJsonAsync(HttpResponseMessage response)
    {
        var json = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        return JsonDocument.Parse(json).RootElement;
    }

    // ── Multipart Upload ────────────────────────────────────────────────

    [Fact]
    public async Task MultipartUpload_AsAdmin_Returns201WithFileInfo()
    {
        using var client = _fixture.CreateAdminClient();
        var bucketId = await CreateBucketAsync(client);

        using var content = CreateMultipartContent("file", "hello.txt", "Hello, World!");
        var response = await client.PostAsync($"/api/buckets/{bucketId}/upload", content, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var body = await ParseJsonAsync(response);
        var uploaded = body.GetProperty("uploaded");
        uploaded.GetArrayLength().Should().Be(1);

        var file = uploaded[0];
        file.GetProperty("path").GetString().Should().Be("hello.txt");
        file.GetProperty("name").GetString().Should().Be("hello.txt");
        file.GetProperty("size").GetInt64().Should().Be(13); // "Hello, World!" = 13 bytes
        file.GetProperty("mime_type").GetString().Should().Be("text/plain");
        file.TryGetProperty("short_code", out var sc).Should().BeTrue();
        sc.GetString().Should().HaveLength(6);
        file.TryGetProperty("short_url", out var su).Should().BeTrue();
        su.GetString().Should().StartWith("/s/");
    }

    [Fact]
    public async Task MultipartUpload_MultipleFiles_ReturnsAll()
    {
        using var client = _fixture.CreateAdminClient();
        var bucketId = await CreateBucketAsync(client);

        var multipart = new MultipartFormDataContent();

        var file1 = new ByteArrayContent(Encoding.UTF8.GetBytes("file one"));
        file1.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        multipart.Add(file1, "file", "one.txt");

        var file2 = new ByteArrayContent(Encoding.UTF8.GetBytes("file two"));
        file2.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        multipart.Add(file2, "file", "two.txt");

        var response = await client.PostAsync($"/api/buckets/{bucketId}/upload", multipart, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var body = await ParseJsonAsync(response);
        body.GetProperty("uploaded").GetArrayLength().Should().Be(2);
    }

    [Fact]
    public async Task MultipartUpload_CustomFieldName_SetsPath()
    {
        using var client = _fixture.CreateAdminClient();
        var bucketId = await CreateBucketAsync(client);

        using var content = CreateMultipartContent("src/main.rs", "ignored.txt", "fn main() {}");
        var response = await client.PostAsync($"/api/buckets/{bucketId}/upload", content, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var body = await ParseJsonAsync(response);
        var file = body.GetProperty("uploaded")[0];
        file.GetProperty("path").GetString().Should().Be("src/main.rs");
    }

    [Fact]
    public async Task MultipartUpload_GenericFieldName_UsesFileName()
    {
        using var client = _fixture.CreateAdminClient();
        var bucketId = await CreateBucketAsync(client);

        using var content = CreateMultipartContent("upload", "data.json", "{}");
        var response = await client.PostAsync($"/api/buckets/{bucketId}/upload", content, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var body = await ParseJsonAsync(response);
        var file = body.GetProperty("uploaded")[0];
        file.GetProperty("path").GetString().Should().Be("data.json");
    }

    [Fact]
    public async Task MultipartUpload_WithoutAuth_Returns403()
    {
        using var adminClient = _fixture.CreateAdminClient();
        var bucketId = await CreateBucketAsync(adminClient);

        using var content = CreateMultipartContent("file", "test.txt", "test");
        var response = await _fixture.Client.PostAsync($"/api/buckets/{bucketId}/upload", content, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task MultipartUpload_ReuploadSamePath_Overwrites()
    {
        using var client = _fixture.CreateAdminClient();
        var bucketId = await CreateBucketAsync(client);

        // Upload first version
        using var content1 = CreateMultipartContent("file", "doc.txt", "version 1");
        var response1 = await client.PostAsync($"/api/buckets/{bucketId}/upload", content1, TestContext.Current.CancellationToken);
        response1.StatusCode.Should().Be(HttpStatusCode.Created);

        var body1 = await ParseJsonAsync(response1);
        var shortCode1 = body1.GetProperty("uploaded")[0].GetProperty("short_code").GetString();

        // Upload second version with same filename
        using var content2 = CreateMultipartContent("file", "doc.txt", "version 2 is longer");
        var response2 = await client.PostAsync($"/api/buckets/{bucketId}/upload", content2, TestContext.Current.CancellationToken);
        response2.StatusCode.Should().Be(HttpStatusCode.Created);

        var body2 = await ParseJsonAsync(response2);
        var file2 = body2.GetProperty("uploaded")[0];
        file2.GetProperty("size").GetInt64().Should().Be(Encoding.UTF8.GetByteCount("version 2 is longer"));

        // Short code should be preserved
        file2.GetProperty("short_code").GetString().Should().Be(shortCode1);
    }

    [Fact]
    public async Task MultipartUpload_NonexistentBucket_Returns404()
    {
        using var client = _fixture.CreateAdminClient();

        using var content = CreateMultipartContent("file", "test.txt", "test");
        var response = await client.PostAsync("/api/buckets/nonexistent/upload", content, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ── Stream Upload ───────────────────────────────────────────────────

    [Fact]
    public async Task StreamUpload_WithFilename_Returns201()
    {
        using var client = _fixture.CreateAdminClient();
        var bucketId = await CreateBucketAsync(client);

        var content = new ByteArrayContent(Encoding.UTF8.GetBytes("stream content"));
        content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");

        var response = await client.PutAsync($"/api/buckets/{bucketId}/upload/stream?filename=stream.txt", content, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var body = await ParseJsonAsync(response);
        var uploaded = body.GetProperty("uploaded");
        uploaded.GetArrayLength().Should().Be(1);
        uploaded[0].GetProperty("path").GetString().Should().Be("stream.txt");
        uploaded[0].GetProperty("mime_type").GetString().Should().Be("text/plain");
    }

    [Fact]
    public async Task StreamUpload_MissingFilename_Returns400()
    {
        using var client = _fixture.CreateAdminClient();
        var bucketId = await CreateBucketAsync(client);

        var content = new ByteArrayContent(Encoding.UTF8.GetBytes("test"));
        var response = await client.PutAsync($"/api/buckets/{bucketId}/upload/stream", content, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task StreamUpload_WithoutAuth_Returns403()
    {
        using var adminClient = _fixture.CreateAdminClient();
        var bucketId = await CreateBucketAsync(adminClient);

        var content = new ByteArrayContent(Encoding.UTF8.GetBytes("test"));
        var response = await _fixture.Client.PutAsync($"/api/buckets/{bucketId}/upload/stream?filename=test.txt", content, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ── Upload with API Key ─────────────────────────────────────────────

    [Fact]
    public async Task MultipartUpload_WithApiKey_Works()
    {
        using var admin = _fixture.CreateAdminClient();

        // Create an API key
        var keyResp = await admin.PostAsJsonAsync("/api/keys", new { name = "uploader" }, TestContext.Current.CancellationToken);
        keyResp.StatusCode.Should().Be(HttpStatusCode.Created);
        var keyBody = await ParseJsonAsync(keyResp);
        var apiKey = keyBody.GetProperty("key").GetString()!;

        using var apiClient = _fixture.CreateAuthenticatedClient(apiKey);

        // Create a bucket with this key
        var bucketResp = await apiClient.PostAsJsonAsync("/api/buckets", new { name = "key-upload-test" }, TestContext.Current.CancellationToken);
        bucketResp.StatusCode.Should().Be(HttpStatusCode.Created);
        var bucketBody = await ParseJsonAsync(bucketResp);
        var bucketId = bucketBody.GetProperty("id").GetString()!;

        // Upload with the API key
        using var content = CreateMultipartContent("file", "key-file.txt", "uploaded with key");
        var response = await apiClient.PostAsync($"/api/buckets/{bucketId}/upload", content, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }
}
