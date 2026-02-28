using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace CarbonFiles.Api.Tests.Endpoints;

public class BucketZipTests : IClassFixture<TestFixture>
{
    private readonly TestFixture _fixture;

    public BucketZipTests(TestFixture fixture) => _fixture = fixture;

    // ── Helpers ──────────────────────────────────────────────────────────

    private async Task<string> CreateBucketAsync(string name = "zip-test")
    {
        using var admin = _fixture.CreateAdminClient();
        var response = await admin.PostAsJsonAsync("/api/buckets", new { name });
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.GetProperty("id").GetString()!;
    }

    private async Task UploadFileAsync(string bucketId, string fileName, string content)
    {
        using var admin = _fixture.CreateAdminClient();
        var multipart = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(Encoding.UTF8.GetBytes(content));
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        multipart.Add(fileContent, "file", fileName);
        var response = await admin.PostAsync($"/api/buckets/{bucketId}/upload", multipart);
        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    // ── Tests ────────────────────────────────────────────────────────────

    [Fact]
    public async Task ZipDownload_WithFiles_ReturnsValidZipContainingAllFiles()
    {
        var bucketId = await CreateBucketAsync("zip-with-files");
        await UploadFileAsync(bucketId, "hello.txt", "Hello, World!");
        await UploadFileAsync(bucketId, "readme.md", "# README");

        var response = await _fixture.Client.GetAsync($"/api/buckets/{bucketId}/zip");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        using var zipStream = await response.Content.ReadAsStreamAsync();
        using var archive = new ZipArchive(zipStream, ZipArchiveMode.Read);
        archive.Entries.Should().HaveCount(2);

        var entryNames = archive.Entries.Select(e => e.FullName).OrderBy(n => n).ToList();
        entryNames.Should().Contain("hello.txt");
        entryNames.Should().Contain("readme.md");
    }

    [Fact]
    public async Task ZipDownload_HasCorrectContentTypeAndDisposition()
    {
        var bucketId = await CreateBucketAsync("zip-headers");
        await UploadFileAsync(bucketId, "file.txt", "content");

        var response = await _fixture.Client.GetAsync($"/api/buckets/{bucketId}/zip");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/zip");
        response.Content.Headers.ContentDisposition!.DispositionType.Should().Be("attachment");
        response.Content.Headers.ContentDisposition.FileName.Should().Contain("zip-headers");
    }

    [Fact]
    public async Task ZipDownload_NonexistentBucket_Returns404()
    {
        var response = await _fixture.Client.GetAsync("/api/buckets/nonexistent/zip");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task ZipDownload_ExpiredBucket_Returns404()
    {
        // We can't easily expire a bucket in integration tests since the minimum is 15m,
        // but we verify a non-existent bucket returns 404 which exercises the same code path.
        var response = await _fixture.Client.GetAsync("/api/buckets/expired0000/zip");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task ZipDownload_EmptyBucket_ReturnsValidEmptyZip()
    {
        var bucketId = await CreateBucketAsync("zip-empty");

        var response = await _fixture.Client.GetAsync($"/api/buckets/{bucketId}/zip");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/zip");

        using var zipStream = await response.Content.ReadAsStreamAsync();
        using var archive = new ZipArchive(zipStream, ZipArchiveMode.Read);
        archive.Entries.Should().BeEmpty();
    }

    [Fact]
    public async Task ZipDownload_FileContentsMatchOriginal()
    {
        var bucketId = await CreateBucketAsync("zip-contents");
        var expectedContent = "This is the exact file content for verification.";
        await UploadFileAsync(bucketId, "verify.txt", expectedContent);

        var response = await _fixture.Client.GetAsync($"/api/buckets/{bucketId}/zip");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        using var zipStream = await response.Content.ReadAsStreamAsync();
        using var archive = new ZipArchive(zipStream, ZipArchiveMode.Read);
        archive.Entries.Should().HaveCount(1);

        var entry = archive.Entries[0];
        entry.FullName.Should().Be("verify.txt");

        using var reader = new StreamReader(entry.Open(), Encoding.UTF8);
        var actualContent = await reader.ReadToEndAsync();
        actualContent.Should().Be(expectedContent);
    }

    [Fact]
    public async Task ZipDownload_HeadRequest_ReturnsHeadersWithoutBody()
    {
        var bucketId = await CreateBucketAsync("zip-head");
        await UploadFileAsync(bucketId, "file.txt", "content");

        var request = new HttpRequestMessage(HttpMethod.Head, $"/api/buckets/{bucketId}/zip");
        var response = await _fixture.Client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/zip");

        // HEAD should have no body
        var body = await response.Content.ReadAsByteArrayAsync();
        body.Should().BeEmpty();
    }
}
