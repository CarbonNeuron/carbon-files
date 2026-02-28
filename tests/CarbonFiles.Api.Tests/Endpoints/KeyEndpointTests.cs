using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace CarbonFiles.Api.Tests.Endpoints;

public class KeyEndpointTests : IClassFixture<TestFixture>
{
    private static readonly JsonSerializerOptions SnakeCaseOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    private readonly TestFixture _fixture;

    public KeyEndpointTests(TestFixture fixture) => _fixture = fixture;

    // ── Create ──────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateKey_AsAdmin_Returns201WithFullKey()
    {
        using var client = _fixture.CreateAdminClient();
        var response = await client.PostAsJsonAsync("/api/keys", new { name = "test-agent" });

        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var json = await response.Content.ReadAsStringAsync();
        json.Should().Contain("\"key\":");
        json.Should().Contain("cf4_");
        json.Should().Contain("\"prefix\":");
        json.Should().Contain("\"name\":");

        // Verify the key is a full key with three parts (cf4_{prefix}_{secret})
        using var doc = JsonDocument.Parse(json);
        var key = doc.RootElement.GetProperty("key").GetString()!;
        key.Should().StartWith("cf4_");
        key.Split('_').Should().HaveCount(3);

        // Verify Location header
        response.Headers.Location!.ToString().Should().Contain("/api/keys/cf4_");
    }

    [Fact]
    public async Task CreateKey_NoAuth_Returns403()
    {
        var response = await _fixture.Client.PostAsJsonAsync("/api/keys", new { name = "test" });
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task CreateKey_EmptyName_Returns400()
    {
        using var client = _fixture.CreateAdminClient();
        var response = await client.PostAsJsonAsync("/api/keys", new { name = "" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var json = await response.Content.ReadAsStringAsync();
        json.Should().Contain("\"error\":");
    }

    [Fact]
    public async Task CreateKey_WhitespaceName_Returns400()
    {
        using var client = _fixture.CreateAdminClient();
        var response = await client.PostAsJsonAsync("/api/keys", new { name = "   " });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ── List ────────────────────────────────────────────────────────────

    [Fact]
    public async Task ListKeys_AsAdmin_ReturnsPaginatedList()
    {
        using var client = _fixture.CreateAdminClient();

        // Create a key first
        await client.PostAsJsonAsync("/api/keys", new { name = "list-test" });

        var response = await client.GetAsync("/api/keys");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await response.Content.ReadAsStringAsync();
        json.Should().Contain("\"items\":");
        json.Should().Contain("\"total\":");
        json.Should().Contain("\"limit\":");
        json.Should().Contain("\"offset\":");
    }

    [Fact]
    public async Task ListKeys_NoAuth_Returns403()
    {
        var response = await _fixture.Client.GetAsync("/api/keys");
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task ListKeys_SecretNotReturned()
    {
        using var client = _fixture.CreateAdminClient();

        // Create a key
        var createResponse = await client.PostAsJsonAsync("/api/keys", new { name = "secret-check" });
        var createJson = await createResponse.Content.ReadAsStringAsync();
        using var createDoc = JsonDocument.Parse(createJson);
        var fullKey = createDoc.RootElement.GetProperty("key").GetString()!;

        // List keys
        var listResponse = await client.GetAsync("/api/keys");
        var listJson = await listResponse.Content.ReadAsStringAsync();

        // The full key (with secret) should NOT appear in list response
        listJson.Should().NotContain(fullKey);
        // But the prefix should appear
        var prefix = createDoc.RootElement.GetProperty("prefix").GetString()!;
        listJson.Should().Contain(prefix);
    }

    [Fact]
    public async Task ListKeys_PaginationWorks()
    {
        using var client = _fixture.CreateAdminClient();

        // Create several keys
        for (int i = 0; i < 3; i++)
            await client.PostAsJsonAsync("/api/keys", new { name = $"page-test-{i}" });

        // Request with limit=1
        var response = await client.GetAsync("/api/keys?limit=1&offset=0");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var items = doc.RootElement.GetProperty("items");
        items.GetArrayLength().Should().Be(1);
        doc.RootElement.GetProperty("total").GetInt32().Should().BeGreaterThanOrEqualTo(3);
    }

    [Fact]
    public async Task ListKeys_SortByName()
    {
        using var client = _fixture.CreateAdminClient();

        // Create keys with known names
        await client.PostAsJsonAsync("/api/keys", new { name = "alpha-sort" });
        await client.PostAsJsonAsync("/api/keys", new { name = "zeta-sort" });

        var response = await client.GetAsync("/api/keys?sort=name&order=asc");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var items = doc.RootElement.GetProperty("items");
        items.GetArrayLength().Should().BeGreaterThanOrEqualTo(2);

        // First item should be alphabetically earlier
        var names = new List<string>();
        foreach (var item in items.EnumerateArray())
            names.Add(item.GetProperty("name").GetString()!);

        names.Should().BeInAscendingOrder();
    }

    // ── Delete ──────────────────────────────────────────────────────────

    [Fact]
    public async Task DeleteKey_AsAdmin_Returns204()
    {
        using var client = _fixture.CreateAdminClient();

        // Create a key
        var createResponse = await client.PostAsJsonAsync("/api/keys", new { name = "to-delete" });
        var createJson = await createResponse.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(createJson);
        var prefix = doc.RootElement.GetProperty("prefix").GetString()!;

        // Delete using prefix
        var deleteResponse = await client.DeleteAsync($"/api/keys/{prefix}");
        deleteResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Verify it's gone
        var getResponse = await client.GetAsync($"/api/keys/{prefix}/usage");
        getResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task DeleteKey_NotFound_Returns404()
    {
        using var client = _fixture.CreateAdminClient();
        var response = await client.DeleteAsync("/api/keys/cf4_nonexist");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task DeleteKey_NoAuth_Returns403()
    {
        var response = await _fixture.Client.DeleteAsync("/api/keys/cf4_whatever");
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ── Usage ───────────────────────────────────────────────────────────

    [Fact]
    public async Task GetKeyUsage_AsAdmin_ReturnsUsageStats()
    {
        using var client = _fixture.CreateAdminClient();

        // Create a key
        var createResponse = await client.PostAsJsonAsync("/api/keys", new { name = "usage-test" });
        var createJson = await createResponse.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(createJson);
        var prefix = doc.RootElement.GetProperty("prefix").GetString()!;

        // Get usage
        var usageResponse = await client.GetAsync($"/api/keys/{prefix}/usage");
        usageResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var usageJson = await usageResponse.Content.ReadAsStringAsync();
        usageJson.Should().Contain("\"prefix\":");
        usageJson.Should().Contain("\"name\":\"usage-test\"");
        usageJson.Should().Contain("\"bucket_count\":");
        usageJson.Should().Contain("\"file_count\":");
        usageJson.Should().Contain("\"total_size\":");
        usageJson.Should().Contain("\"total_downloads\":");
        usageJson.Should().Contain("\"buckets\":");
    }

    [Fact]
    public async Task GetKeyUsage_SecretNotReturned()
    {
        using var client = _fixture.CreateAdminClient();

        // Create a key
        var createResponse = await client.PostAsJsonAsync("/api/keys", new { name = "usage-secret-check" });
        var createJson = await createResponse.Content.ReadAsStringAsync();
        using var createDoc = JsonDocument.Parse(createJson);
        var fullKey = createDoc.RootElement.GetProperty("key").GetString()!;
        var prefix = createDoc.RootElement.GetProperty("prefix").GetString()!;

        // Get usage — should not contain full key
        var usageResponse = await client.GetAsync($"/api/keys/{prefix}/usage");
        var usageJson = await usageResponse.Content.ReadAsStringAsync();
        usageJson.Should().NotContain(fullKey);
    }

    [Fact]
    public async Task GetKeyUsage_NotFound_Returns404()
    {
        using var client = _fixture.CreateAdminClient();
        var response = await client.GetAsync("/api/keys/cf4_nonexist/usage");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetKeyUsage_NoAuth_Returns403()
    {
        var response = await _fixture.Client.GetAsync("/api/keys/cf4_whatever/usage");
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}
