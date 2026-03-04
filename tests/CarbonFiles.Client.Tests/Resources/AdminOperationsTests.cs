using System.Net;
using CarbonFiles.Client;
using CarbonFiles.Client.Models;
using FluentAssertions;
using Xunit;

namespace CarbonFiles.Client.Tests.Resources;

public class AdminOperationsTests
{
    private static (CarbonFilesClient Client, MockHandler Handler) Create()
    {
        var handler = new MockHandler();
        var client = new CarbonFilesClient(new CarbonFilesClientOptions
        {
            BaseAddress = new Uri("https://example.com"),
            ApiKey = "test-key",
            HttpClient = new HttpClient(handler) { BaseAddress = new Uri("https://example.com") }
        });
        return (client, handler);
    }

    // --- API Keys ---

    [Fact]
    public async Task Keys_CreateAsync_PostsToApiKeys_ReturnsApiKeyResponse()
    {
        var (client, handler) = Create();
        handler.Enqueue(HttpStatusCode.Created,
            """{"key":"cf4_abc123","prefix":"abc1","name":"my-key","created_at":"2026-01-01T00:00:00Z"}""");

        var result = await client.Keys.CreateAsync(
            new CreateApiKeyRequest { Name = "my-key" },
            TestContext.Current.CancellationToken);

        result.Key.Should().Be("cf4_abc123");
        result.Prefix.Should().Be("abc1");
        result.Name.Should().Be("my-key");
        handler.Requests.Should().ContainSingle();
        handler.Requests[0].Method.Should().Be(HttpMethod.Post);
        handler.Requests[0].RequestUri!.PathAndQuery.Should().Be("/api/keys");
    }

    [Fact]
    public async Task Keys_ListAsync_GetsPaginatedList()
    {
        var (client, handler) = Create();
        handler.Enqueue(HttpStatusCode.OK,
            """{"items":[{"prefix":"abc1","name":"my-key","created_at":"2026-01-01T00:00:00Z","bucket_count":2,"file_count":5,"total_size":1024}],"total":1,"limit":10,"offset":0}""");

        var result = await client.Keys.ListAsync(
            pagination: new PaginationOptions { Limit = 10, Offset = 0 },
            ct: TestContext.Current.CancellationToken);

        result.Items.Should().HaveCount(1);
        result.Items[0].Prefix.Should().Be("abc1");
        result.Items[0].Name.Should().Be("my-key");
        result.Total.Should().Be(1);
        handler.Requests[0].Method.Should().Be(HttpMethod.Get);
        var query = handler.Requests[0].RequestUri!.Query;
        query.Should().Contain("limit=10");
        query.Should().Contain("offset=0");
    }

    [Fact]
    public async Task Keys_RevokeAsync_SendsDeleteToApiKeysPrefix()
    {
        var (client, handler) = Create();
        handler.Enqueue(HttpStatusCode.NoContent, "");

        await client.Keys["abc1"].RevokeAsync(TestContext.Current.CancellationToken);

        handler.Requests.Should().ContainSingle();
        handler.Requests[0].Method.Should().Be(HttpMethod.Delete);
        handler.Requests[0].RequestUri!.PathAndQuery.Should().Be("/api/keys/abc1");
    }

    [Fact]
    public async Task Keys_GetUsageAsync_ReturnsUsageStats()
    {
        var (client, handler) = Create();
        handler.Enqueue(HttpStatusCode.OK,
            """{"prefix":"abc1","name":"my-key","created_at":"2026-01-01T00:00:00Z","bucket_count":2,"file_count":5,"total_size":1024,"total_downloads":100,"buckets":[]}""");

        var result = await client.Keys["abc1"].GetUsageAsync(TestContext.Current.CancellationToken);

        result.Prefix.Should().Be("abc1");
        result.BucketCount.Should().Be(2);
        result.TotalDownloads.Should().Be(100);
        handler.Requests[0].Method.Should().Be(HttpMethod.Get);
        handler.Requests[0].RequestUri!.PathAndQuery.Should().Be("/api/keys/abc1/usage");
    }

    [Fact]
    public async Task Keys_Indexer_EscapesPrefix()
    {
        var (client, handler) = Create();
        handler.Enqueue(HttpStatusCode.NoContent, "");

        await client.Keys["a/b"].RevokeAsync(TestContext.Current.CancellationToken);

        handler.Requests[0].RequestUri!.PathAndQuery.Should().Be("/api/keys/a%2Fb");
    }

    // --- Stats ---

    [Fact]
    public async Task Stats_GetAsync_ReturnsStatsResponse()
    {
        var (client, handler) = Create();
        handler.Enqueue(HttpStatusCode.OK,
            """{"total_buckets":10,"total_files":50,"total_size":102400,"total_keys":3,"total_downloads":500,"storage_by_owner":[]}""");

        var result = await client.Stats.GetAsync(TestContext.Current.CancellationToken);

        result.TotalBuckets.Should().Be(10);
        result.TotalFiles.Should().Be(50);
        result.TotalSize.Should().Be(102400);
        result.TotalKeys.Should().Be(3);
        result.TotalDownloads.Should().Be(500);
        handler.Requests.Should().ContainSingle();
        handler.Requests[0].Method.Should().Be(HttpMethod.Get);
        handler.Requests[0].RequestUri!.PathAndQuery.Should().Be("/api/stats");
    }

    // --- Short URLs ---

    [Fact]
    public async Task ShortUrls_DeleteAsync_SendsDeleteToApiShortCode()
    {
        var (client, handler) = Create();
        handler.Enqueue(HttpStatusCode.NoContent, "");

        await client.ShortUrls["abc123"].DeleteAsync(TestContext.Current.CancellationToken);

        handler.Requests.Should().ContainSingle();
        handler.Requests[0].Method.Should().Be(HttpMethod.Delete);
        handler.Requests[0].RequestUri!.PathAndQuery.Should().Be("/api/short/abc123");
    }

    [Fact]
    public async Task ShortUrls_Indexer_EscapesCode()
    {
        var (client, handler) = Create();
        handler.Enqueue(HttpStatusCode.NoContent, "");

        await client.ShortUrls["a/b"].DeleteAsync(TestContext.Current.CancellationToken);

        handler.Requests[0].RequestUri!.PathAndQuery.Should().Be("/api/short/a%2Fb");
    }

    // --- Dashboard ---

    [Fact]
    public async Task Dashboard_CreateTokenAsync_PostsToTokensDashboard()
    {
        var (client, handler) = Create();
        handler.Enqueue(HttpStatusCode.Created,
            """{"token":"eyJhbGciOiJIUzI1NiJ9.test","expires_at":"2026-01-02T00:00:00Z"}""");

        var result = await client.Dashboard.CreateTokenAsync(
            new CreateDashboardTokenRequest { ExpiresIn = "24h" },
            TestContext.Current.CancellationToken);

        result.Token.Should().StartWith("eyJ");
        result.ExpiresAt.Should().Be(DateTimeOffset.Parse("2026-01-02T00:00:00Z"));
        handler.Requests.Should().ContainSingle();
        handler.Requests[0].Method.Should().Be(HttpMethod.Post);
        handler.Requests[0].RequestUri!.PathAndQuery.Should().Be("/api/tokens/dashboard");
    }

    [Fact]
    public async Task Dashboard_CreateTokenAsync_NullRequest_PostsEmptyBody()
    {
        var (client, handler) = Create();
        handler.Enqueue(HttpStatusCode.Created,
            """{"token":"eyJhbGciOiJIUzI1NiJ9.test","expires_at":"2026-01-02T00:00:00Z"}""");

        var result = await client.Dashboard.CreateTokenAsync(ct: TestContext.Current.CancellationToken);

        result.Token.Should().NotBeEmpty();
        handler.Requests[0].Method.Should().Be(HttpMethod.Post);
        handler.Requests[0].RequestUri!.PathAndQuery.Should().Be("/api/tokens/dashboard");
    }

    [Fact]
    public async Task Dashboard_GetCurrentUserAsync_ReturnsTokenInfo()
    {
        var (client, handler) = Create();
        handler.Enqueue(HttpStatusCode.OK,
            """{"scope":"admin","expires_at":"2026-01-02T00:00:00Z"}""");

        var result = await client.Dashboard.GetCurrentUserAsync(TestContext.Current.CancellationToken);

        result.Scope.Should().Be("admin");
        result.ExpiresAt.Should().Be(DateTimeOffset.Parse("2026-01-02T00:00:00Z"));
        handler.Requests.Should().ContainSingle();
        handler.Requests[0].Method.Should().Be(HttpMethod.Get);
        handler.Requests[0].RequestUri!.PathAndQuery.Should().Be("/api/tokens/dashboard/me");
    }

    // --- Upload Tokens ---

    [Fact]
    public async Task UploadTokens_CreateAsync_PostsToBucketTokens()
    {
        var (client, handler) = Create();
        handler.Enqueue(HttpStatusCode.Created,
            """{"token":"cfu_abc123","bucket_id":"b1","expires_at":"2026-01-02T00:00:00Z","max_uploads":10,"uploads_used":0}""");

        var result = await client.Buckets["b1"].Tokens.CreateAsync(
            new CreateUploadTokenRequest { ExpiresIn = "1h", MaxUploads = 10 },
            TestContext.Current.CancellationToken);

        result.Token.Should().Be("cfu_abc123");
        result.BucketId.Should().Be("b1");
        result.MaxUploads.Should().Be(10);
        result.UploadsUsed.Should().Be(0);
        handler.Requests.Should().ContainSingle();
        handler.Requests[0].Method.Should().Be(HttpMethod.Post);
        handler.Requests[0].RequestUri!.PathAndQuery.Should().Be("/api/buckets/b1/tokens");
    }

    [Fact]
    public async Task UploadTokens_CreateAsync_EscapesBucketId()
    {
        var (client, handler) = Create();
        handler.Enqueue(HttpStatusCode.Created,
            """{"token":"cfu_abc123","bucket_id":"a/b","expires_at":"2026-01-02T00:00:00Z","uploads_used":0}""");

        await client.Buckets["a/b"].Tokens.CreateAsync(
            new CreateUploadTokenRequest { ExpiresIn = "1h" },
            TestContext.Current.CancellationToken);

        handler.Requests[0].RequestUri!.PathAndQuery.Should().Be("/api/buckets/a%2Fb/tokens");
    }
}
