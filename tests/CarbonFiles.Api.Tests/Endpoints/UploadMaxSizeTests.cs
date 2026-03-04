using System.Data;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using CarbonFiles.Infrastructure.Data;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CarbonFiles.Api.Tests.Endpoints;

/// <summary>
/// Tests MaxUploadSize enforcement for both multipart and stream upload endpoints.
/// Uses a separate fixture with MaxUploadSize=1024 (1KB).
/// </summary>
public class UploadMaxSizeTests : IAsyncLifetime
{
    private const long MaxUploadSize = 1024; // 1KB limit

    private WebApplicationFactory<Program> _factory = null!;
    private SqliteConnection _keepAlive = null!;
    private string _tempDir = null!;
    private HttpClient _client = null!;

    public async ValueTask InitializeAsync()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"cf_maxsize_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);

        var dbName = $"MaxSizeTest_{Guid.NewGuid():N}";
        var connectionString = $"Data Source={dbName};Mode=Memory;Cache=Shared";
        _keepAlive = new SqliteConnection(connectionString);
        await _keepAlive.OpenAsync();
        DatabaseInitializer.Initialize(_keepAlive);

        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.ConfigureServices(services =>
            {
                var descriptor = services.SingleOrDefault(d => d.ServiceType == typeof(IDbConnection));
                if (descriptor != null) services.Remove(descriptor);

                var connStr = connectionString;
                services.AddScoped<IDbConnection>(_ =>
                {
                    var conn = new SqliteConnection(connStr);
                    conn.Open();
                    return conn;
                });
            });
            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["CarbonFiles:AdminKey"] = "test-admin-key",
                    ["CarbonFiles:DataDir"] = _tempDir,
                    ["CarbonFiles:DbPath"] = Path.Combine(_tempDir, "test.db"),
                    ["CarbonFiles:MaxUploadSize"] = MaxUploadSize.ToString(),
                });
            });
        });

        _client = _factory.CreateClient();
        _client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", "test-admin-key");
    }

    public async ValueTask DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
        await _keepAlive.DisposeAsync();
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    private async Task<string> CreateBucketAsync()
    {
        var response = await _client.PostAsJsonAsync("/api/buckets",
            new { name = $"maxsize-test-{Guid.NewGuid():N}" }, TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var json = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.GetProperty("id").GetString()!;
    }

    // ── Multipart ───────────────────────────────────────────────────────

    [Fact]
    public async Task MultipartUpload_ExceedsMaxSize_Returns413()
    {
        var bucketId = await CreateBucketAsync();
        var largeData = new string('X', (int)MaxUploadSize + 1); // 1025 bytes > 1024 limit

        var multipart = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(Encoding.UTF8.GetBytes(largeData));
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        multipart.Add(fileContent, "file", "toobig.txt");

        var response = await _client.PostAsync($"/api/buckets/{bucketId}/upload", multipart, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.RequestEntityTooLarge);
    }

    [Fact]
    public async Task MultipartUpload_WithinMaxSize_Returns201()
    {
        var bucketId = await CreateBucketAsync();
        var smallData = new string('X', 100); // 100 bytes < 1024 limit

        var multipart = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(Encoding.UTF8.GetBytes(smallData));
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        multipart.Add(fileContent, "file", "small.txt");

        var response = await _client.PostAsync($"/api/buckets/{bucketId}/upload", multipart, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task MultipartUpload_ExactlyAtMaxSize_Returns201()
    {
        var bucketId = await CreateBucketAsync();
        var exactData = new byte[(int)MaxUploadSize]; // exactly 1024 bytes

        var multipart = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(exactData);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        multipart.Add(fileContent, "file", "exact.bin");

        var response = await _client.PostAsync($"/api/buckets/{bucketId}/upload", multipart, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    // ── Stream upload ───────────────────────────────────────────────────

    [Fact]
    public async Task StreamUpload_ExceedsMaxSize_Returns413()
    {
        var bucketId = await CreateBucketAsync();
        var largeData = new byte[(int)MaxUploadSize + 1];

        var content = new ByteArrayContent(largeData);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");

        var response = await _client.PutAsync(
            $"/api/buckets/{bucketId}/upload/stream?filename=toobig.bin", content, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.RequestEntityTooLarge);
    }

    [Fact]
    public async Task StreamUpload_WithinMaxSize_Returns201()
    {
        var bucketId = await CreateBucketAsync();
        var smallData = new byte[100];

        var content = new ByteArrayContent(smallData);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");

        var response = await _client.PutAsync(
            $"/api/buckets/{bucketId}/upload/stream?filename=small.bin", content, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    // ── Verify no partial file left on disk after rejection ─────────────

    [Fact]
    public async Task MultipartUpload_ExceedsMaxSize_DoesNotLeaveFileOnDisk()
    {
        var bucketId = await CreateBucketAsync();
        var largeData = new byte[(int)MaxUploadSize + 512];

        var multipart = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(largeData);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        multipart.Add(fileContent, "file", "phantom.bin");

        await _client.PostAsync($"/api/buckets/{bucketId}/upload", multipart, TestContext.Current.CancellationToken);

        // The bucket directory should not contain the rejected file
        var bucketDir = Path.Combine(_tempDir, bucketId);
        if (Directory.Exists(bucketDir))
        {
            var files = Directory.GetFiles(bucketDir, "*", SearchOption.AllDirectories);
            files.Should().NotContain(f => f.Contains("phantom"));
        }
    }
}
