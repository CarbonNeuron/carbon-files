using CarbonFiles.Core.Interfaces;
using CarbonFiles.Core.Models;
using CarbonFiles.Core.Models.Responses;
using Microsoft.AspNetCore.SignalR;

namespace CarbonFiles.Api.Hubs;

public sealed class HubNotificationService : INotificationService
{
    private readonly IHubContext<FileHub> _hub;

    public HubNotificationService(IHubContext<FileHub> hub) => _hub = hub;

    public async Task NotifyFileCreated(string bucketId, BucketFile file)
    {
        await _hub.Clients.Group($"bucket:{bucketId}").SendAsync("FileCreated", bucketId, file);
        await _hub.Clients.Group($"file:{bucketId}:{file.Path}").SendAsync("FileCreated", bucketId, file);
        await _hub.Clients.Group("global").SendAsync("FileCreated", bucketId, file);
    }

    public async Task NotifyFileUpdated(string bucketId, BucketFile file)
    {
        await _hub.Clients.Group($"bucket:{bucketId}").SendAsync("FileUpdated", bucketId, file);
        await _hub.Clients.Group($"file:{bucketId}:{file.Path}").SendAsync("FileUpdated", bucketId, file);
        await _hub.Clients.Group("global").SendAsync("FileUpdated", bucketId, file);
    }

    public async Task NotifyFileDeleted(string bucketId, string path)
    {
        await _hub.Clients.Group($"bucket:{bucketId}").SendAsync("FileDeleted", bucketId, path);
        await _hub.Clients.Group($"file:{bucketId}:{path}").SendAsync("FileDeleted", bucketId, path);
        await _hub.Clients.Group("global").SendAsync("FileDeleted", bucketId, path);
    }

    public async Task NotifyBucketCreated(Bucket bucket)
    {
        await _hub.Clients.Group("global").SendAsync("BucketCreated", bucket);
    }

    public async Task NotifyBucketUpdated(string bucketId, BucketChanges changes)
    {
        await _hub.Clients.Group($"bucket:{bucketId}").SendAsync("BucketUpdated", bucketId, changes);
        await _hub.Clients.Group("global").SendAsync("BucketUpdated", bucketId, changes);
    }

    public async Task NotifyBucketDeleted(string bucketId)
    {
        await _hub.Clients.Group($"bucket:{bucketId}").SendAsync("BucketDeleted", bucketId);
        await _hub.Clients.Group("global").SendAsync("BucketDeleted", bucketId);
    }
}
