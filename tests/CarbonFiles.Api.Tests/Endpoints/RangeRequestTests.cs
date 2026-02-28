using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace CarbonFiles.Api.Tests.Endpoints;

public class RangeRequestTests : IntegrationTestBase
{

    // ── Helpers ──────────────────────────────────────────────────────────

    private async Task<string> CreateBucketAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync("/api/buckets", new { name = $"range-test-{Guid.NewGuid():N}" }, TestContext.Current.CancellationToken);
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

    /// <summary>
    /// Creates a bucket with a 1000-byte file containing bytes 0-255 repeating (0,1,2,...255,0,1,...).
    /// Returns (bucketId, fileName, fileBytes).
    /// </summary>
    private async Task<(string BucketId, string FileName, byte[] FileBytes)> SetupTestFileAsync()
    {
        using var admin = Fixture.CreateAdminClient();
        var bucketId = await CreateBucketAsync(admin);

        var fileBytes = new byte[1000];
        for (var i = 0; i < fileBytes.Length; i++)
            fileBytes[i] = (byte)(i % 256);

        const string fileName = "range-test.bin";
        await UploadFileAsync(admin, bucketId, fileName, fileBytes);

        return (bucketId, fileName, fileBytes);
    }

    // ── Range Request Tests ─────────────────────────────────────────────

    [Fact]
    public async Task Download_WithRangeHeader_Returns206()
    {
        var (bucketId, fileName, fileBytes) = await SetupTestFileAsync();

        // Range: bytes=0-99 -> first 100 bytes
        var request = new HttpRequestMessage(HttpMethod.Get,
            $"/api/buckets/{bucketId}/files/{fileName}/content");
        request.Headers.Range = new RangeHeaderValue(0, 99);

        var response = await Fixture.Client.SendAsync(request, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.PartialContent);

        var body = await response.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken);
        body.Length.Should().Be(100);
        body.Should().BeEquivalentTo(fileBytes[..100]);
    }

    [Fact]
    public async Task Download_WithOpenEndRange_Returns206()
    {
        var (bucketId, fileName, fileBytes) = await SetupTestFileAsync();

        // Range: bytes=500- -> byte 500 to end
        var request = new HttpRequestMessage(HttpMethod.Get,
            $"/api/buckets/{bucketId}/files/{fileName}/content");
        request.Headers.Range = new RangeHeaderValue(500, null);

        var response = await Fixture.Client.SendAsync(request, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.PartialContent);

        var body = await response.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken);
        body.Length.Should().Be(500);
        body.Should().BeEquivalentTo(fileBytes[500..]);
    }

    [Fact]
    public async Task Download_WithSuffixRange_Returns206()
    {
        var (bucketId, fileName, fileBytes) = await SetupTestFileAsync();

        // Range: bytes=-100 -> last 100 bytes
        var request = new HttpRequestMessage(HttpMethod.Get,
            $"/api/buckets/{bucketId}/files/{fileName}/content");
        request.Headers.Range = new RangeHeaderValue(null, 100);

        var response = await Fixture.Client.SendAsync(request, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.PartialContent);

        var body = await response.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken);
        body.Length.Should().Be(100);
        body.Should().BeEquivalentTo(fileBytes[900..]);
    }

    [Fact]
    public async Task Download_WithInvalidRange_Returns416()
    {
        var (bucketId, fileName, _) = await SetupTestFileAsync();

        // Range: bytes=9999-99999 on a 1000-byte file
        var request = new HttpRequestMessage(HttpMethod.Get,
            $"/api/buckets/{bucketId}/files/{fileName}/content");
        request.Headers.Range = new RangeHeaderValue(9999, 99999);

        var response = await Fixture.Client.SendAsync(request, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.RequestedRangeNotSatisfiable);
    }

    [Fact]
    public async Task Download_ContentRangeHeader_IsCorrect()
    {
        var (bucketId, fileName, _) = await SetupTestFileAsync();

        var request = new HttpRequestMessage(HttpMethod.Get,
            $"/api/buckets/{bucketId}/files/{fileName}/content");
        request.Headers.Range = new RangeHeaderValue(100, 199);

        var response = await Fixture.Client.SendAsync(request, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.PartialContent);

        // Verify Content-Range header format: bytes 100-199/1000
        response.Content.Headers.ContentRange.Should().NotBeNull();
        response.Content.Headers.ContentRange!.From.Should().Be(100);
        response.Content.Headers.ContentRange.To.Should().Be(199);
        response.Content.Headers.ContentRange.Length.Should().Be(1000);
        response.Content.Headers.ContentRange.Unit.Should().Be("bytes");

        // Verify Content-Length matches range
        response.Content.Headers.ContentLength.Should().Be(100);
    }

    [Fact]
    public async Task Download_InvalidRange_ContentRangeHeader_ShowsTotal()
    {
        var (bucketId, fileName, _) = await SetupTestFileAsync();

        var request = new HttpRequestMessage(HttpMethod.Get,
            $"/api/buckets/{bucketId}/files/{fileName}/content");
        request.Headers.Range = new RangeHeaderValue(9999, 99999);

        var response = await Fixture.Client.SendAsync(request, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.RequestedRangeNotSatisfiable);

        // Content-Range: bytes */1000
        response.Content.Headers.ContentRange.Should().NotBeNull();
        response.Content.Headers.ContentRange!.Length.Should().Be(1000);
        response.Content.Headers.ContentRange.HasRange.Should().BeFalse();
    }

    [Fact]
    public async Task Download_WithIfRange_MatchingETag_Returns206()
    {
        var (bucketId, fileName, fileBytes) = await SetupTestFileAsync();

        // First, get the ETag
        var firstResponse = await Fixture.Client.GetAsync(
            $"/api/buckets/{bucketId}/files/{fileName}/content", TestContext.Current.CancellationToken);
        firstResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var etag = firstResponse.Headers.ETag!.Tag;

        // Now make a range request with matching If-Range
        var request = new HttpRequestMessage(HttpMethod.Get,
            $"/api/buckets/{bucketId}/files/{fileName}/content");
        request.Headers.Range = new RangeHeaderValue(0, 49);
        request.Headers.IfRange = new RangeConditionHeaderValue(new EntityTagHeaderValue(etag));

        var response = await Fixture.Client.SendAsync(request, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.PartialContent);

        var body = await response.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken);
        body.Length.Should().Be(50);
        body.Should().BeEquivalentTo(fileBytes[..50]);
    }

    [Fact]
    public async Task Download_WithIfRange_StaleETag_Returns200Full()
    {
        var (bucketId, fileName, fileBytes) = await SetupTestFileAsync();

        // Use a fake/stale ETag
        var request = new HttpRequestMessage(HttpMethod.Get,
            $"/api/buckets/{bucketId}/files/{fileName}/content");
        request.Headers.Range = new RangeHeaderValue(0, 49);
        request.Headers.IfRange = new RangeConditionHeaderValue(new EntityTagHeaderValue("\"stale-etag\""));

        var response = await Fixture.Client.SendAsync(request, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken);
        body.Length.Should().Be(1000);
        body.Should().BeEquivalentTo(fileBytes);
    }

    [Fact]
    public async Task Download_RangeRequest_PreservesETagHeader()
    {
        var (bucketId, fileName, _) = await SetupTestFileAsync();

        var request = new HttpRequestMessage(HttpMethod.Get,
            $"/api/buckets/{bucketId}/files/{fileName}/content");
        request.Headers.Range = new RangeHeaderValue(0, 99);

        var response = await Fixture.Client.SendAsync(request, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.PartialContent);
        response.Headers.ETag.Should().NotBeNull();
    }

    [Fact]
    public async Task Download_RangeRequest_PreservesAcceptRangesHeader()
    {
        var (bucketId, fileName, _) = await SetupTestFileAsync();

        var request = new HttpRequestMessage(HttpMethod.Get,
            $"/api/buckets/{bucketId}/files/{fileName}/content");
        request.Headers.Range = new RangeHeaderValue(0, 99);

        var response = await Fixture.Client.SendAsync(request, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.PartialContent);
        response.Headers.AcceptRanges.Should().Contain("bytes");
    }

    [Fact]
    public async Task Download_RangeEndBeyondFileSize_ClampedToEnd()
    {
        var (bucketId, fileName, fileBytes) = await SetupTestFileAsync();

        // Range: bytes=900-9999 on a 1000-byte file -> should clamp to 900-999
        var request = new HttpRequestMessage(HttpMethod.Get,
            $"/api/buckets/{bucketId}/files/{fileName}/content");
        request.Headers.Range = new RangeHeaderValue(900, 9999);

        var response = await Fixture.Client.SendAsync(request, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.PartialContent);

        var body = await response.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken);
        body.Length.Should().Be(100);
        body.Should().BeEquivalentTo(fileBytes[900..]);

        response.Content.Headers.ContentRange!.From.Should().Be(900);
        response.Content.Headers.ContentRange.To.Should().Be(999);
        response.Content.Headers.ContentRange.Length.Should().Be(1000);
    }

    // ── HEAD Request Tests ──────────────────────────────────────────────

    [Fact]
    public async Task Head_ReturnsHeadersNoBody()
    {
        var (bucketId, fileName, _) = await SetupTestFileAsync();

        var request = new HttpRequestMessage(HttpMethod.Head,
            $"/api/buckets/{bucketId}/files/{fileName}/content");

        var response = await Fixture.Client.SendAsync(request, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        // Headers should be present
        response.Headers.ETag.Should().NotBeNull();
        response.Content.Headers.ContentLength.Should().Be(1000);

        // Body should be empty
        var body = await response.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken);
        body.Length.Should().Be(0);
    }

    [Fact]
    public async Task Head_ReturnsCorrectContentType()
    {
        var (bucketId, fileName, _) = await SetupTestFileAsync();

        var request = new HttpRequestMessage(HttpMethod.Head,
            $"/api/buckets/{bucketId}/files/{fileName}/content");

        var response = await Fixture.Client.SendAsync(request, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/octet-stream");
    }

    [Fact]
    public async Task Head_NonexistentFile_Returns404()
    {
        using var admin = Fixture.CreateAdminClient();
        var bucketId = await CreateBucketAsync(admin);

        var request = new HttpRequestMessage(HttpMethod.Head,
            $"/api/buckets/{bucketId}/files/nonexistent.bin/content");

        var response = await Fixture.Client.SendAsync(request, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
