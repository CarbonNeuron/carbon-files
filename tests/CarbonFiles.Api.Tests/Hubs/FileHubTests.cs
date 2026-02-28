using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Xunit;

namespace CarbonFiles.Api.Tests.Hubs;

public class FileHubTests : IClassFixture<TestFixture>
{
    private readonly TestFixture _fixture;

    public FileHubTests(TestFixture fixture) => _fixture = fixture;

    // ── Helpers ──────────────────────────────────────────────────────────

    private static async Task<JsonElement> ParseJsonAsync(HttpResponseMessage response)
    {
        var json = await response.Content.ReadAsStringAsync();
        return JsonDocument.Parse(json).RootElement;
    }

    private async Task<string> CreateBucketAsync(HttpClient client, string name = "hub-test")
    {
        var response = await client.PostAsJsonAsync("/api/buckets", new { name });
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await ParseJsonAsync(response);
        return body.GetProperty("id").GetString()!;
    }

    private static async Task<HttpResponseMessage> UploadFileAsync(HttpClient client, string bucketId, string filename, string content)
    {
        var multipart = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(Encoding.UTF8.GetBytes(content));
        fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");
        multipart.Add(fileContent, "file", filename);
        return await client.PostAsync($"/api/buckets/{bucketId}/upload", multipart);
    }

    private HubConnection CreateHubConnection(string hubUrl)
    {
        return new HubConnectionBuilder()
            .WithUrl(hubUrl, options =>
            {
                options.HttpMessageHandlerFactory = _ => _fixture.GetHandler();
            })
            .Build();
    }

    // ── Hub Endpoint Availability ────────────────────────────────────────

    [Fact]
    public async Task Hub_Negotiate_ReturnsOk()
    {
        using var client = _fixture.CreateAdminClient();

        // The negotiate endpoint should be reachable
        var response = await client.PostAsync("/hub/files/negotiate?negotiateVersion=1", null);

        // SignalR negotiate endpoint returns 200 with connection info
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        doc.RootElement.TryGetProperty("connectionId", out _).Should().BeTrue();
    }

    // ── Integration: Upload triggers FileCreated notification ─────────

    [Fact]
    public async Task UploadFile_SubscribedToBucket_ReceivesFileCreatedEvent()
    {
        using var admin = _fixture.CreateAdminClient();
        var bucketId = await CreateBucketAsync(admin, "signalr-upload-test");

        var hubUrl = _fixture.GetServerUrl() + "/hub/files";
        await using var connection = CreateHubConnection(hubUrl);

        var receivedEvent = new TaskCompletionSource<(string BucketId, JsonElement File)>();

        connection.On<string, JsonElement>("FileCreated", (bId, file) =>
        {
            receivedEvent.TrySetResult((bId, file));
        });

        await connection.StartAsync();
        await connection.InvokeAsync("SubscribeToBucket", bucketId);

        // Upload a file via multipart
        var uploadResp = await UploadFileAsync(admin, bucketId, "test.txt", "hello world");
        uploadResp.StatusCode.Should().Be(HttpStatusCode.Created);

        // Wait for the event (with timeout)
        var completedTask = await Task.WhenAny(receivedEvent.Task, Task.Delay(5000));
        completedTask.Should().Be(receivedEvent.Task, "Should receive FileCreated event within 5 seconds");

        var (eventBucketId, eventFile) = receivedEvent.Task.Result;
        eventBucketId.Should().Be(bucketId);
        eventFile.GetProperty("path").GetString().Should().Be("test.txt");

        await connection.StopAsync();
    }

    [Fact]
    public async Task ReUploadFile_SubscribedToFile_ReceivesFileUpdatedEvent()
    {
        using var admin = _fixture.CreateAdminClient();
        var bucketId = await CreateBucketAsync(admin, "signalr-reupload-test");

        // Upload initial file
        var uploadResp1 = await UploadFileAsync(admin, bucketId, "doc.txt", "version 1");
        uploadResp1.StatusCode.Should().Be(HttpStatusCode.Created);

        // Connect and subscribe to file
        var hubUrl = _fixture.GetServerUrl() + "/hub/files";
        await using var connection = CreateHubConnection(hubUrl);

        var receivedEvent = new TaskCompletionSource<(string BucketId, JsonElement File)>();

        connection.On<string, JsonElement>("FileUpdated", (bId, file) =>
        {
            receivedEvent.TrySetResult((bId, file));
        });

        await connection.StartAsync();
        await connection.InvokeAsync("SubscribeToFile", bucketId, "doc.txt");

        // Re-upload the file (same path triggers update)
        var uploadResp2 = await UploadFileAsync(admin, bucketId, "doc.txt", "version 2 with more content");
        uploadResp2.StatusCode.Should().Be(HttpStatusCode.Created);

        // Wait for the event
        var completedTask = await Task.WhenAny(receivedEvent.Task, Task.Delay(5000));
        completedTask.Should().Be(receivedEvent.Task, "Should receive FileUpdated event within 5 seconds");

        var (eventBucketId, eventFile) = receivedEvent.Task.Result;
        eventBucketId.Should().Be(bucketId);
        eventFile.GetProperty("path").GetString().Should().Be("doc.txt");

        await connection.StopAsync();
    }

    [Fact]
    public async Task DeleteFile_SubscribedToBucket_ReceivesFileDeletedEvent()
    {
        using var admin = _fixture.CreateAdminClient();
        var bucketId = await CreateBucketAsync(admin, "signalr-delete-test");

        // Upload a file first
        var uploadResp = await UploadFileAsync(admin, bucketId, "delete-me.txt", "to be deleted");
        uploadResp.StatusCode.Should().Be(HttpStatusCode.Created);

        // Connect and subscribe
        var hubUrl = _fixture.GetServerUrl() + "/hub/files";
        await using var connection = CreateHubConnection(hubUrl);

        var receivedEvent = new TaskCompletionSource<(string BucketId, string Path)>();

        connection.On<string, string>("FileDeleted", (bId, path) =>
        {
            receivedEvent.TrySetResult((bId, path));
        });

        await connection.StartAsync();
        await connection.InvokeAsync("SubscribeToBucket", bucketId);

        // Delete the file
        var deleteResp = await admin.DeleteAsync($"/api/buckets/{bucketId}/files/delete-me.txt");
        deleteResp.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Wait for the event
        var completedTask = await Task.WhenAny(receivedEvent.Task, Task.Delay(5000));
        completedTask.Should().Be(receivedEvent.Task, "Should receive FileDeleted event within 5 seconds");

        var (eventBucketId, eventPath) = receivedEvent.Task.Result;
        eventBucketId.Should().Be(bucketId);
        eventPath.Should().Be("delete-me.txt");

        await connection.StopAsync();
    }

    [Fact]
    public async Task SubscribeToAll_WithoutAdminAuth_ThrowsHubException()
    {
        var hubUrl = _fixture.GetServerUrl() + "/hub/files";
        await using var connection = CreateHubConnection(hubUrl);

        await connection.StartAsync();

        // SubscribeToAll without auth token should throw
        var act = () => connection.InvokeAsync("SubscribeToAll");
        await act.Should().ThrowAsync<HubException>()
            .WithMessage("*Admin authentication required*");

        await connection.StopAsync();
    }

    [Fact]
    public async Task SubscribeToAll_WithAdminToken_ReceivesBucketCreatedEvent()
    {
        var hubUrl = _fixture.GetServerUrl() + "/hub/files?access_token=test-admin-key";
        await using var connection = CreateHubConnection(hubUrl);

        var receivedEvent = new TaskCompletionSource<JsonElement>();

        connection.On<JsonElement>("BucketCreated", bucket =>
        {
            receivedEvent.TrySetResult(bucket);
        });

        await connection.StartAsync();
        await connection.InvokeAsync("SubscribeToAll");

        // Create a bucket
        using var admin = _fixture.CreateAdminClient();
        var createResp = await admin.PostAsJsonAsync("/api/buckets", new { name = "global-notify-test" });
        createResp.StatusCode.Should().Be(HttpStatusCode.Created);

        // Wait for the event
        var completedTask = await Task.WhenAny(receivedEvent.Task, Task.Delay(5000));
        completedTask.Should().Be(receivedEvent.Task, "Should receive BucketCreated event within 5 seconds");

        var eventBucket = receivedEvent.Task.Result;
        eventBucket.GetProperty("name").GetString().Should().Be("global-notify-test");

        await connection.StopAsync();
    }

    [Fact]
    public async Task UnsubscribeFromBucket_StopsReceivingEvents()
    {
        using var admin = _fixture.CreateAdminClient();
        var bucketId = await CreateBucketAsync(admin, "signalr-unsub-test");

        var hubUrl = _fixture.GetServerUrl() + "/hub/files";
        await using var connection = CreateHubConnection(hubUrl);

        var receivedEvents = new List<string>();
        var firstEventReceived = new TaskCompletionSource<bool>();

        connection.On<string, JsonElement>("FileCreated", (_, file) =>
        {
            receivedEvents.Add(file.GetProperty("path").GetString()!);
            firstEventReceived.TrySetResult(true);
        });

        await connection.StartAsync();
        await connection.InvokeAsync("SubscribeToBucket", bucketId);

        // Upload first file (should receive)
        var uploadResp1 = await UploadFileAsync(admin, bucketId, "file1.txt", "content 1");
        uploadResp1.StatusCode.Should().Be(HttpStatusCode.Created);

        // Wait for the first event
        var completed = await Task.WhenAny(firstEventReceived.Task, Task.Delay(5000));
        completed.Should().Be(firstEventReceived.Task, "Should receive first FileCreated event");
        receivedEvents.Should().HaveCount(1);

        // Unsubscribe
        await connection.InvokeAsync("UnsubscribeFromBucket", bucketId);

        // Upload second file (should NOT receive)
        var uploadResp2 = await UploadFileAsync(admin, bucketId, "file2.txt", "content 2");
        uploadResp2.StatusCode.Should().Be(HttpStatusCode.Created);

        // Wait a bit and verify no new event
        await Task.Delay(1000);
        receivedEvents.Should().HaveCount(1);

        await connection.StopAsync();
    }

    [Fact]
    public async Task BucketDeleted_SubscribedToBucket_ReceivesBucketDeletedEvent()
    {
        using var admin = _fixture.CreateAdminClient();
        var bucketId = await CreateBucketAsync(admin, "signalr-bucket-delete-test");

        var hubUrl = _fixture.GetServerUrl() + "/hub/files";
        await using var connection = CreateHubConnection(hubUrl);

        var receivedEvent = new TaskCompletionSource<string>();

        connection.On<string>("BucketDeleted", bucketIdReceived =>
        {
            receivedEvent.TrySetResult(bucketIdReceived);
        });

        await connection.StartAsync();
        await connection.InvokeAsync("SubscribeToBucket", bucketId);

        // Delete the bucket
        var deleteResp = await admin.DeleteAsync($"/api/buckets/{bucketId}");
        deleteResp.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Wait for the event
        var completedTask = await Task.WhenAny(receivedEvent.Task, Task.Delay(5000));
        completedTask.Should().Be(receivedEvent.Task, "Should receive BucketDeleted event within 5 seconds");

        receivedEvent.Task.Result.Should().Be(bucketId);

        await connection.StopAsync();
    }

    [Fact]
    public async Task BucketUpdated_SubscribedToBucket_ReceivesBucketUpdatedEvent()
    {
        using var admin = _fixture.CreateAdminClient();
        var bucketId = await CreateBucketAsync(admin, "signalr-bucket-update-test");

        var hubUrl = _fixture.GetServerUrl() + "/hub/files";
        await using var connection = CreateHubConnection(hubUrl);

        var receivedEvent = new TaskCompletionSource<(string BucketId, JsonElement Changes)>();

        connection.On<string, JsonElement>("BucketUpdated", (bId, changes) =>
        {
            receivedEvent.TrySetResult((bId, changes));
        });

        await connection.StartAsync();
        await connection.InvokeAsync("SubscribeToBucket", bucketId);

        // Update the bucket
        var updateResp = await admin.PatchAsJsonAsync($"/api/buckets/{bucketId}",
            new { name = "updated-signalr-test" });
        updateResp.StatusCode.Should().Be(HttpStatusCode.OK);

        // Wait for the event
        var completedTask = await Task.WhenAny(receivedEvent.Task, Task.Delay(5000));
        completedTask.Should().Be(receivedEvent.Task, "Should receive BucketUpdated event within 5 seconds");

        var (eventBucketId, eventChanges) = receivedEvent.Task.Result;
        eventBucketId.Should().Be(bucketId);
        eventChanges.GetProperty("name").GetString().Should().Be("updated-signalr-test");

        await connection.StopAsync();
    }
}
