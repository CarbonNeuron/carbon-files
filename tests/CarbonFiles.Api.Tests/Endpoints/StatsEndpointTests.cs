using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace CarbonFiles.Api.Tests.Endpoints;

public class StatsEndpointTests : IClassFixture<TestFixture>
{
    private readonly TestFixture _fixture;

    public StatsEndpointTests(TestFixture fixture) => _fixture = fixture;

    // ── Helpers ──────────────────────────────────────────────────────────

    private static async Task<JsonElement> ParseJsonAsync(HttpResponseMessage response)
    {
        var json = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        return JsonDocument.Parse(json).RootElement;
    }

    private async Task<(HttpClient Client, string ApiKey, string Prefix)> CreateApiKeyClientAsync(string name)
    {
        using var admin = _fixture.CreateAdminClient();
        var response = await admin.PostAsJsonAsync("/api/keys", new { name }, TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var json = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        using var doc = JsonDocument.Parse(json);
        var apiKey = doc.RootElement.GetProperty("key").GetString()!;
        var prefix = doc.RootElement.GetProperty("prefix").GetString()!;

        var client = _fixture.CreateAuthenticatedClient(apiKey);
        return (client, apiKey, prefix);
    }

    private async Task<string> CreateBucketAsync(HttpClient client, string name = "stats-bucket")
    {
        var response = await client.PostAsJsonAsync("/api/buckets", new { name, expires_in = "never" }, TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var json = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.GetProperty("id").GetString()!;
    }

    private async Task UploadFileAsync(HttpClient client, string bucketId, string fileName, string content)
    {
        var multipart = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(Encoding.UTF8.GetBytes(content));
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        multipart.Add(fileContent, "file", fileName);

        var response = await client.PostAsync($"/api/buckets/{bucketId}/upload", multipart, TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    // ── Tests ────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetStats_AsAdmin_ReturnsStats()
    {
        using var admin = _fixture.CreateAdminClient();

        // Create some data: API key, bucket, files
        var (apiClient, _, _) = await CreateApiKeyClientAsync("stats-agent");
        using (apiClient)
        {
            var bucketId = await CreateBucketAsync(apiClient, "stats-data-bucket");
            await UploadFileAsync(apiClient, bucketId, "file1.txt", "hello world");
            await UploadFileAsync(apiClient, bucketId, "file2.txt", "more data here");
        }

        var response = await admin.GetAsync("/api/stats", TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await ParseJsonAsync(response);
        body.GetProperty("total_buckets").GetInt32().Should().BeGreaterThanOrEqualTo(1);
        body.GetProperty("total_files").GetInt32().Should().BeGreaterThanOrEqualTo(2);
        body.GetProperty("total_size").GetInt64().Should().BeGreaterThanOrEqualTo(1);
        body.GetProperty("total_keys").GetInt32().Should().BeGreaterThanOrEqualTo(1);
        body.GetProperty("total_downloads").GetInt64().Should().BeGreaterThanOrEqualTo(0);
        body.GetProperty("storage_by_owner").GetArrayLength().Should().BeGreaterThanOrEqualTo(1);
    }

    [Fact]
    public async Task GetStats_NoAuth_Returns403()
    {
        var response = await _fixture.Client.GetAsync("/api/stats", TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task GetStats_ApiKeyAuth_Returns403()
    {
        var (apiClient, _, _) = await CreateApiKeyClientAsync("non-admin-stats");
        using (apiClient)
        {
            var response = await apiClient.GetAsync("/api/stats", TestContext.Current.CancellationToken);
            response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        }
    }

    [Fact]
    public async Task GetStats_ReturnsStorageByOwner()
    {
        // Create data owned by two different keys
        var (client1, _, _) = await CreateApiKeyClientAsync("stats-owner-a");
        var (client2, _, _) = await CreateApiKeyClientAsync("stats-owner-b");

        using (client1)
        using (client2)
        {
            var bucketA = await CreateBucketAsync(client1, "owner-a-bucket");
            await UploadFileAsync(client1, bucketA, "a.txt", "owner a data");

            var bucketB = await CreateBucketAsync(client2, "owner-b-bucket");
            await UploadFileAsync(client2, bucketB, "b.txt", "owner b data here");
        }

        using var admin = _fixture.CreateAdminClient();
        var response = await admin.GetAsync("/api/stats", TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await ParseJsonAsync(response);
        var owners = body.GetProperty("storage_by_owner");
        owners.GetArrayLength().Should().BeGreaterThanOrEqualTo(2);

        // Verify each owner entry has the expected fields
        foreach (var ownerEntry in owners.EnumerateArray())
        {
            ownerEntry.TryGetProperty("owner", out _).Should().BeTrue();
            ownerEntry.TryGetProperty("bucket_count", out _).Should().BeTrue();
            ownerEntry.TryGetProperty("file_count", out _).Should().BeTrue();
            ownerEntry.TryGetProperty("total_size", out _).Should().BeTrue();
        }

        // Check that stats-owner-a and stats-owner-b are present
        var ownerNames = new List<string>();
        foreach (var ownerEntry in owners.EnumerateArray())
            ownerNames.Add(ownerEntry.GetProperty("owner").GetString()!);

        ownerNames.Should().Contain("stats-owner-a");
        ownerNames.Should().Contain("stats-owner-b");
    }

    [Fact]
    public async Task GetStats_ReturnsSnakeCaseJson()
    {
        using var admin = _fixture.CreateAdminClient();
        var response = await admin.GetAsync("/api/stats", TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        // Verify snake_case field names
        json.Should().Contain("\"total_buckets\":");
        json.Should().Contain("\"total_files\":");
        json.Should().Contain("\"total_size\":");
        json.Should().Contain("\"total_keys\":");
        json.Should().Contain("\"total_downloads\":");
        json.Should().Contain("\"storage_by_owner\":");

        // Verify PascalCase is NOT used
        json.Should().NotContain("\"TotalBuckets\"");
        json.Should().NotContain("\"TotalFiles\"");
        json.Should().NotContain("\"StorageByOwner\"");
    }
}
