using System.Text.Json;
using CarbonFiles.Client.Models;
using FluentAssertions;
using Xunit;

namespace CarbonFiles.Client.Tests.Models;

public class SerializationTests
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    [Fact]
    public void Bucket_Deserializes_SnakeCase()
    {
        var json = """{"id":"abc123","name":"test","owner":"admin","description":"desc","created_at":"2026-01-01T00:00:00Z","expires_at":null,"last_used_at":null,"file_count":5,"total_size":1024}""";
        var bucket = JsonSerializer.Deserialize<Bucket>(json, Options)!;
        bucket.Id.Should().Be("abc123");
        bucket.Name.Should().Be("test");
        bucket.Owner.Should().Be("admin");
        bucket.Description.Should().Be("desc");
        bucket.FileCount.Should().Be(5);
        bucket.TotalSize.Should().Be(1024);
    }

    [Fact]
    public void Bucket_Serializes_SnakeCase()
    {
        var bucket = new Bucket
        {
            Id = "abc123",
            Name = "test",
            Owner = "admin",
            FileCount = 5,
            TotalSize = 1024
        };
        var json = JsonSerializer.Serialize(bucket, Options);
        json.Should().Contain("\"id\"").And.Contain("\"file_count\"").And.Contain("\"total_size\"");
        json.Should().NotContain("description"); // null omitted
    }

    [Fact]
    public void BucketFile_Deserializes_SnakeCase()
    {
        var json = """{"path":"docs/readme.md","name":"readme.md","size":256,"mime_type":"text/markdown","short_code":"abc123","short_url":"https://example.com/s/abc123","created_at":"2026-01-01T00:00:00Z","updated_at":"2026-01-02T00:00:00Z"}""";
        var file = JsonSerializer.Deserialize<BucketFile>(json, Options)!;
        file.Path.Should().Be("docs/readme.md");
        file.Name.Should().Be("readme.md");
        file.Size.Should().Be(256);
        file.MimeType.Should().Be("text/markdown");
        file.ShortCode.Should().Be("abc123");
        file.ShortUrl.Should().Be("https://example.com/s/abc123");
    }

    [Fact]
    public void BucketFile_NullOptionalFields_Deserializes()
    {
        var json = """{"path":"file.txt","name":"file.txt","size":100,"mime_type":"text/plain","created_at":"2026-01-01T00:00:00Z","updated_at":"2026-01-01T00:00:00Z"}""";
        var file = JsonSerializer.Deserialize<BucketFile>(json, Options)!;
        file.ShortCode.Should().BeNull();
        file.ShortUrl.Should().BeNull();
    }

    [Fact]
    public void PaginatedResponse_Deserializes()
    {
        var json = """{"items":[{"id":"abc","name":"b","owner":"o","created_at":"2026-01-01T00:00:00Z","file_count":0,"total_size":0}],"total":1,"limit":50,"offset":0}""";
        var page = JsonSerializer.Deserialize<PaginatedResponse<Bucket>>(json, Options)!;
        page.Items.Should().HaveCount(1);
        page.Items[0].Id.Should().Be("abc");
        page.Total.Should().Be(1);
        page.Limit.Should().Be(50);
        page.Offset.Should().Be(0);
    }

    [Fact]
    public void PaginatedResponse_EmptyItems_Deserializes()
    {
        var json = """{"items":[],"total":0,"limit":50,"offset":0}""";
        var page = JsonSerializer.Deserialize<PaginatedResponse<Bucket>>(json, Options)!;
        page.Items.Should().BeEmpty();
        page.Total.Should().Be(0);
    }

    [Fact]
    public void CreateBucketRequest_Serializes_SnakeCase()
    {
        var req = new CreateBucketRequest { Name = "test", ExpiresIn = "1h" };
        var json = JsonSerializer.Serialize(req, Options);
        json.Should().Contain("\"name\"").And.Contain("\"expires_in\"");
        json.Should().NotContain("description"); // null omitted
    }

    [Fact]
    public void UpdateBucketRequest_Serializes_NullsOmitted()
    {
        var req = new UpdateBucketRequest { Name = "updated" };
        var json = JsonSerializer.Serialize(req, Options);
        json.Should().Contain("\"name\"");
        json.Should().NotContain("description");
        json.Should().NotContain("expires_in");
    }

    [Fact]
    public void CreateApiKeyRequest_Serializes()
    {
        var req = new CreateApiKeyRequest { Name = "my-key" };
        var json = JsonSerializer.Serialize(req, Options);
        json.Should().Contain("\"name\":\"my-key\"");
    }

    [Fact]
    public void CreateUploadTokenRequest_Serializes()
    {
        var req = new CreateUploadTokenRequest { ExpiresIn = "1h", MaxUploads = 10 };
        var json = JsonSerializer.Serialize(req, Options);
        json.Should().Contain("\"expires_in\"").And.Contain("\"max_uploads\"");
    }

    [Fact]
    public void CreateDashboardTokenRequest_Serializes()
    {
        var req = new CreateDashboardTokenRequest { ExpiresIn = "24h" };
        var json = JsonSerializer.Serialize(req, Options);
        json.Should().Contain("\"expires_in\":\"24h\"");
    }

    [Fact]
    public void ErrorResponse_Deserializes()
    {
        var json = """{"error":"not found","hint":"check bucket id"}""";
        var err = JsonSerializer.Deserialize<ErrorResponse>(json, Options)!;
        err.Error.Should().Be("not found");
        err.Hint.Should().Be("check bucket id");
    }

    [Fact]
    public void ErrorResponse_WithoutHint_Deserializes()
    {
        var json = """{"error":"unauthorized"}""";
        var err = JsonSerializer.Deserialize<ErrorResponse>(json, Options)!;
        err.Error.Should().Be("unauthorized");
        err.Hint.Should().BeNull();
    }

    [Fact]
    public void HealthResponse_Deserializes()
    {
        var json = """{"status":"ok","uptime_seconds":3600,"db":"ok"}""";
        var health = JsonSerializer.Deserialize<HealthResponse>(json, Options)!;
        health.Status.Should().Be("ok");
        health.UptimeSeconds.Should().Be(3600);
        health.Db.Should().Be("ok");
    }

    [Fact]
    public void UploadResponse_Deserializes()
    {
        var json = """{"uploaded":[{"path":"file.txt","name":"file.txt","size":100,"mime_type":"text/plain","created_at":"2026-01-01T00:00:00Z","updated_at":"2026-01-01T00:00:00Z"}]}""";
        var resp = JsonSerializer.Deserialize<UploadResponse>(json, Options)!;
        resp.Uploaded.Should().HaveCount(1);
        resp.Uploaded[0].Path.Should().Be("file.txt");
    }

    [Fact]
    public void BucketDetailResponse_Deserializes()
    {
        var json = """{"id":"abc","name":"b","owner":"o","created_at":"2026-01-01T00:00:00Z","file_count":1,"total_size":100,"files":[{"path":"f.txt","name":"f.txt","size":100,"mime_type":"text/plain","created_at":"2026-01-01T00:00:00Z","updated_at":"2026-01-01T00:00:00Z"}],"has_more_files":false}""";
        var detail = JsonSerializer.Deserialize<BucketDetailResponse>(json, Options)!;
        detail.Id.Should().Be("abc");
        detail.Files.Should().HaveCount(1);
        detail.HasMoreFiles.Should().BeFalse();
    }

    [Fact]
    public void DirectoryListingResponse_Deserializes()
    {
        var json = """{"files":[],"folders":["sub"],"total_files":0,"total_folders":1,"limit":50,"offset":0}""";
        var dir = JsonSerializer.Deserialize<DirectoryListingResponse>(json, Options)!;
        dir.Folders.Should().ContainSingle().Which.Should().Be("sub");
        dir.TotalFiles.Should().Be(0);
        dir.TotalFolders.Should().Be(1);
    }

    [Fact]
    public void ApiKeyResponse_Deserializes()
    {
        var json = """{"key":"cf4_abc_secret","prefix":"cf4_abc","name":"test","created_at":"2026-01-01T00:00:00Z"}""";
        var key = JsonSerializer.Deserialize<ApiKeyResponse>(json, Options)!;
        key.Key.Should().Be("cf4_abc_secret");
        key.Prefix.Should().Be("cf4_abc");
        key.Name.Should().Be("test");
    }

    [Fact]
    public void ApiKeyListItem_Deserializes()
    {
        var json = """{"prefix":"cf4_abc","name":"test","created_at":"2026-01-01T00:00:00Z","last_used_at":"2026-01-02T00:00:00Z","bucket_count":3,"file_count":10,"total_size":5000}""";
        var item = JsonSerializer.Deserialize<ApiKeyListItem>(json, Options)!;
        item.Prefix.Should().Be("cf4_abc");
        item.BucketCount.Should().Be(3);
        item.FileCount.Should().Be(10);
        item.TotalSize.Should().Be(5000);
    }

    [Fact]
    public void ApiKeyUsageResponse_Deserializes()
    {
        var json = """{"prefix":"cf4_abc","name":"test","created_at":"2026-01-01T00:00:00Z","bucket_count":2,"file_count":5,"total_size":2000,"total_downloads":100,"buckets":[{"id":"b1","name":"bucket1","owner":"admin","created_at":"2026-01-01T00:00:00Z","file_count":5,"total_size":2000}]}""";
        var usage = JsonSerializer.Deserialize<ApiKeyUsageResponse>(json, Options)!;
        usage.TotalDownloads.Should().Be(100);
        usage.Buckets.Should().HaveCount(1);
        usage.Buckets[0].Id.Should().Be("b1");
    }

    [Fact]
    public void UploadTokenResponse_Deserializes()
    {
        var json = """{"token":"cfu_abc","bucket_id":"xyz","expires_at":"2026-01-02T00:00:00Z","max_uploads":10,"uploads_used":3}""";
        var tok = JsonSerializer.Deserialize<UploadTokenResponse>(json, Options)!;
        tok.Token.Should().Be("cfu_abc");
        tok.BucketId.Should().Be("xyz");
        tok.MaxUploads.Should().Be(10);
        tok.UploadsUsed.Should().Be(3);
    }

    [Fact]
    public void DashboardTokenResponse_Deserializes()
    {
        var json = """{"token":"jwt.token.here","expires_at":"2026-01-02T00:00:00Z"}""";
        var tok = JsonSerializer.Deserialize<DashboardTokenResponse>(json, Options)!;
        tok.Token.Should().Be("jwt.token.here");
    }

    [Fact]
    public void DashboardTokenInfo_Deserializes()
    {
        var json = """{"scope":"admin","expires_at":"2026-01-02T00:00:00Z"}""";
        var info = JsonSerializer.Deserialize<DashboardTokenInfo>(json, Options)!;
        info.Scope.Should().Be("admin");
    }

    [Fact]
    public void StatsResponse_Deserializes()
    {
        var json = """{"total_buckets":10,"total_files":100,"total_size":9999,"total_keys":3,"total_downloads":50,"storage_by_owner":[{"owner":"admin","bucket_count":5,"file_count":50,"total_size":5000}]}""";
        var stats = JsonSerializer.Deserialize<StatsResponse>(json, Options)!;
        stats.TotalBuckets.Should().Be(10);
        stats.TotalFiles.Should().Be(100);
        stats.TotalSize.Should().Be(9999);
        stats.TotalKeys.Should().Be(3);
        stats.TotalDownloads.Should().Be(50);
        stats.StorageByOwner.Should().HaveCount(1);
        stats.StorageByOwner[0].Owner.Should().Be("admin");
        stats.StorageByOwner[0].BucketCount.Should().Be(5);
    }

    [Fact]
    public void BucketChanges_Deserializes()
    {
        var json = """{"name":"new-name","description":"updated","expires_at":"2026-06-01T00:00:00Z"}""";
        var changes = JsonSerializer.Deserialize<BucketChanges>(json, Options)!;
        changes.Name.Should().Be("new-name");
        changes.Description.Should().Be("updated");
        changes.ExpiresAt.Should().NotBeNull();
    }

    [Fact]
    public void UploadProgress_CalculatesPercentage()
    {
        var progress = new UploadProgress(50, 100);
        progress.BytesSent.Should().Be(50);
        progress.TotalBytes.Should().Be(100);
        progress.Percentage.Should().Be(50);
    }

    [Fact]
    public void UploadProgress_NullTotal_NullPercentage()
    {
        var progress = new UploadProgress(50, null);
        progress.BytesSent.Should().Be(50);
        progress.TotalBytes.Should().BeNull();
        progress.Percentage.Should().BeNull();
    }

    [Fact]
    public void UploadProgress_ZeroTotal_NullPercentage()
    {
        var progress = new UploadProgress(0, 0);
        progress.Percentage.Should().BeNull();
    }

    [Fact]
    public void PaginationOptions_DefaultsToNull()
    {
        var opts = new PaginationOptions();
        opts.Limit.Should().BeNull();
        opts.Offset.Should().BeNull();
        opts.Sort.Should().BeNull();
        opts.Order.Should().BeNull();
    }
}
