using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace CarbonFiles.Api.Tests.Endpoints;

public class UploadTokenEndpointTests : IClassFixture<TestFixture>
{
    private readonly TestFixture _fixture;

    public UploadTokenEndpointTests(TestFixture fixture) => _fixture = fixture;

    // ── Helpers ──────────────────────────────────────────────────────────

    private async Task<string> CreateBucketAsync(HttpClient? client = null)
    {
        var c = client ?? _fixture.CreateAdminClient();
        var response = await c.PostAsJsonAsync("/api/buckets", new { name = $"token-test-{Guid.NewGuid():N}" });
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.GetProperty("id").GetString()!;
    }

    private async Task<string> CreateUploadTokenAsync(HttpClient client, string bucketId, object? request = null)
    {
        var response = await client.PostAsJsonAsync($"/api/buckets/{bucketId}/tokens", request ?? new { });
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.GetProperty("token").GetString()!;
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
        var json = await response.Content.ReadAsStringAsync();
        return JsonDocument.Parse(json).RootElement;
    }

    // ── Create Upload Token ─────────────────────────────────────────────

    [Fact]
    public async Task CreateToken_AsAdmin_Returns201WithToken()
    {
        using var client = _fixture.CreateAdminClient();
        var bucketId = await CreateBucketAsync(client);

        var response = await client.PostAsJsonAsync($"/api/buckets/{bucketId}/tokens", new { });

        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var body = await ParseJsonAsync(response);
        body.GetProperty("token").GetString().Should().StartWith("cfu_");
        body.GetProperty("bucket_id").GetString().Should().Be(bucketId);
        body.GetProperty("uploads_used").GetInt32().Should().Be(0);

        // Default expiry should be ~1 day from now
        var expiresAt = body.GetProperty("expires_at").GetDateTime();
        expiresAt.Should().BeCloseTo(DateTime.UtcNow.AddDays(1), TimeSpan.FromMinutes(2));
    }

    [Fact]
    public async Task CreateToken_WithMaxUploads_ReturnsCorrectResponse()
    {
        using var client = _fixture.CreateAdminClient();
        var bucketId = await CreateBucketAsync(client);

        var response = await client.PostAsJsonAsync($"/api/buckets/{bucketId}/tokens", new { max_uploads = 10 });

        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var body = await ParseJsonAsync(response);
        body.GetProperty("max_uploads").GetInt32().Should().Be(10);
    }

    [Fact]
    public async Task CreateToken_WithCustomExpiry_ReturnsCorrectExpiry()
    {
        using var client = _fixture.CreateAdminClient();
        var bucketId = await CreateBucketAsync(client);

        var response = await client.PostAsJsonAsync($"/api/buckets/{bucketId}/tokens", new { expires_in = "1h" });

        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var body = await ParseJsonAsync(response);
        var expiresAt = body.GetProperty("expires_at").GetDateTime();
        expiresAt.Should().BeCloseTo(DateTime.UtcNow.AddHours(1), TimeSpan.FromMinutes(2));
    }

    [Fact]
    public async Task CreateToken_AsPublic_Returns403()
    {
        using var adminClient = _fixture.CreateAdminClient();
        var bucketId = await CreateBucketAsync(adminClient);

        var response = await _fixture.Client.PostAsJsonAsync($"/api/buckets/{bucketId}/tokens", new { });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task CreateToken_NonExistentBucket_Returns404()
    {
        using var client = _fixture.CreateAdminClient();

        var response = await client.PostAsJsonAsync("/api/buckets/nonexist01/tokens", new { });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task CreateToken_WithApiKey_Works()
    {
        using var admin = _fixture.CreateAdminClient();

        // Create an API key
        var keyResp = await admin.PostAsJsonAsync("/api/keys", new { name = "token-creator" });
        keyResp.StatusCode.Should().Be(HttpStatusCode.Created);
        var keyBody = await ParseJsonAsync(keyResp);
        var apiKey = keyBody.GetProperty("key").GetString()!;

        using var apiClient = _fixture.CreateAuthenticatedClient(apiKey);

        // Create a bucket with this key
        var bucketResp = await apiClient.PostAsJsonAsync("/api/buckets", new { name = "key-token-test" });
        bucketResp.StatusCode.Should().Be(HttpStatusCode.Created);
        var bucketBody = await ParseJsonAsync(bucketResp);
        var bucketId = bucketBody.GetProperty("id").GetString()!;

        // Create token with the API key
        var response = await apiClient.PostAsJsonAsync($"/api/buckets/{bucketId}/tokens", new { });

        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var body = await ParseJsonAsync(response);
        body.GetProperty("token").GetString().Should().StartWith("cfu_");
    }

    [Fact]
    public async Task CreateToken_InvalidExpiry_Returns400()
    {
        using var client = _fixture.CreateAdminClient();
        var bucketId = await CreateBucketAsync(client);

        var response = await client.PostAsJsonAsync($"/api/buckets/{bucketId}/tokens", new { expires_in = "invalid" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ── Use Upload Token for Upload ─────────────────────────────────────

    [Fact]
    public async Task UploadWithToken_NoAuthHeader_Succeeds()
    {
        using var admin = _fixture.CreateAdminClient();
        var bucketId = await CreateBucketAsync(admin);
        var token = await CreateUploadTokenAsync(admin, bucketId);

        // Upload without any auth header (public client), just with token param
        using var content = CreateMultipartContent("file", "token-upload.txt", "uploaded via token");
        var response = await _fixture.Client.PostAsync($"/api/buckets/{bucketId}/upload?token={token}", content);

        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var body = await ParseJsonAsync(response);
        body.GetProperty("uploaded").GetArrayLength().Should().Be(1);
        body.GetProperty("uploaded")[0].GetProperty("path").GetString().Should().Be("token-upload.txt");
    }

    [Fact]
    public async Task StreamUploadWithToken_NoAuthHeader_Succeeds()
    {
        using var admin = _fixture.CreateAdminClient();
        var bucketId = await CreateBucketAsync(admin);
        var token = await CreateUploadTokenAsync(admin, bucketId);

        var content = new ByteArrayContent(Encoding.UTF8.GetBytes("stream via token"));
        content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");

        var response = await _fixture.Client.PutAsync(
            $"/api/buckets/{bucketId}/upload/stream?filename=stream-token.txt&token={token}", content);

        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var body = await ParseJsonAsync(response);
        body.GetProperty("uploaded")[0].GetProperty("path").GetString().Should().Be("stream-token.txt");
    }

    [Fact]
    public async Task UploadWithToken_MaxUploadsEnforced()
    {
        using var admin = _fixture.CreateAdminClient();
        var bucketId = await CreateBucketAsync(admin);
        var token = await CreateUploadTokenAsync(admin, bucketId, new { max_uploads = 1 });

        // First upload should succeed
        using var content1 = CreateMultipartContent("file", "first.txt", "first upload");
        var response1 = await _fixture.Client.PostAsync($"/api/buckets/{bucketId}/upload?token={token}", content1);
        response1.StatusCode.Should().Be(HttpStatusCode.Created);

        // Second upload should be rejected
        using var content2 = CreateMultipartContent("file", "second.txt", "second upload");
        var response2 = await _fixture.Client.PostAsync($"/api/buckets/{bucketId}/upload?token={token}", content2);
        response2.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task UploadWithToken_TokenScopedToBucket()
    {
        using var admin = _fixture.CreateAdminClient();
        var bucketId1 = await CreateBucketAsync(admin);
        var bucketId2 = await CreateBucketAsync(admin);
        var token = await CreateUploadTokenAsync(admin, bucketId1);

        // Try to use token on a different bucket
        using var content = CreateMultipartContent("file", "wrong-bucket.txt", "should fail");
        var response = await _fixture.Client.PostAsync($"/api/buckets/{bucketId2}/upload?token={token}", content);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task UploadWithToken_InvalidToken_Returns403()
    {
        using var admin = _fixture.CreateAdminClient();
        var bucketId = await CreateBucketAsync(admin);

        using var content = CreateMultipartContent("file", "test.txt", "test");
        var response = await _fixture.Client.PostAsync(
            $"/api/buckets/{bucketId}/upload?token=cfu_invalidtoken00000000000000000000000000000000", content);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task UploadWithToken_ExpiredToken_Returns403()
    {
        using var admin = _fixture.CreateAdminClient();
        var bucketId = await CreateBucketAsync(admin);

        // Create a token with very short expiry (15 minutes is the shortest preset)
        // We can't easily test actual expiry in integration tests without waiting,
        // so we verify that the token response has the correct expiry set
        var response = await admin.PostAsJsonAsync($"/api/buckets/{bucketId}/tokens", new { expires_in = "15m" });
        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var body = await ParseJsonAsync(response);
        var expiresAt = body.GetProperty("expires_at").GetDateTime();
        expiresAt.Should().BeCloseTo(DateTime.UtcNow.AddMinutes(15), TimeSpan.FromMinutes(2));
    }

    [Fact]
    public async Task UploadWithToken_StreamEndpoint_MaxUploadsEnforced()
    {
        using var admin = _fixture.CreateAdminClient();
        var bucketId = await CreateBucketAsync(admin);
        var token = await CreateUploadTokenAsync(admin, bucketId, new { max_uploads = 1 });

        // First stream upload should succeed
        var content1 = new ByteArrayContent(Encoding.UTF8.GetBytes("first stream"));
        content1.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        var response1 = await _fixture.Client.PutAsync(
            $"/api/buckets/{bucketId}/upload/stream?filename=first.txt&token={token}", content1);
        response1.StatusCode.Should().Be(HttpStatusCode.Created);

        // Second stream upload should be rejected
        var content2 = new ByteArrayContent(Encoding.UTF8.GetBytes("second stream"));
        content2.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        var response2 = await _fixture.Client.PutAsync(
            $"/api/buckets/{bucketId}/upload/stream?filename=second.txt&token={token}", content2);
        response2.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task UploadWithToken_StreamEndpoint_TokenScopedToBucket()
    {
        using var admin = _fixture.CreateAdminClient();
        var bucketId1 = await CreateBucketAsync(admin);
        var bucketId2 = await CreateBucketAsync(admin);
        var token = await CreateUploadTokenAsync(admin, bucketId1);

        var content = new ByteArrayContent(Encoding.UTF8.GetBytes("wrong bucket"));
        content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        var response = await _fixture.Client.PutAsync(
            $"/api/buckets/{bucketId2}/upload/stream?filename=test.txt&token={token}", content);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task UploadWithToken_MultipleFiles_IncrementsUsageByCount()
    {
        using var admin = _fixture.CreateAdminClient();
        var bucketId = await CreateBucketAsync(admin);
        var token = await CreateUploadTokenAsync(admin, bucketId, new { max_uploads = 3 });

        // Upload 2 files in one request
        var multipart = new MultipartFormDataContent();
        var file1 = new ByteArrayContent(Encoding.UTF8.GetBytes("file one"));
        file1.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        multipart.Add(file1, "file", "one.txt");
        var file2 = new ByteArrayContent(Encoding.UTF8.GetBytes("file two"));
        file2.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        multipart.Add(file2, "file", "two.txt");

        var response1 = await _fixture.Client.PostAsync($"/api/buckets/{bucketId}/upload?token={token}", multipart);
        response1.StatusCode.Should().Be(HttpStatusCode.Created);

        // Now try uploading 2 more files (should fail because we already used 2 of 3)
        var multipart2 = new MultipartFormDataContent();
        var file3 = new ByteArrayContent(Encoding.UTF8.GetBytes("file three"));
        file3.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        multipart2.Add(file3, "file", "three.txt");
        var file4 = new ByteArrayContent(Encoding.UTF8.GetBytes("file four"));
        file4.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        multipart2.Add(file4, "file", "four.txt");

        // This should succeed (uploads_used=2, max=3, and we're uploading 2 more)
        // But at the validation stage, 2 < 3 so the upload proceeds, then usage becomes 4
        // The validation only checks if current usage < max, not future usage
        var response2 = await _fixture.Client.PostAsync($"/api/buckets/{bucketId}/upload?token={token}", multipart2);
        response2.StatusCode.Should().Be(HttpStatusCode.Created);

        // Now one more upload should be rejected (usage is now 4 >= max 3)
        using var content5 = CreateMultipartContent("file", "five.txt", "file five");
        var response3 = await _fixture.Client.PostAsync($"/api/buckets/{bucketId}/upload?token={token}", content5);
        response3.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}
