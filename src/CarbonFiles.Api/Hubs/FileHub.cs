using CarbonFiles.Core.Interfaces;
using Microsoft.AspNetCore.SignalR;

namespace CarbonFiles.Api.Hubs;

public class FileHub : Hub
{
    private readonly ILogger<FileHub> _logger;

    public FileHub(ILogger<FileHub> logger) => _logger = logger;

    public override Task OnConnectedAsync()
    {
        _logger.LogInformation("SignalR client connected: {ConnectionId}", Context.ConnectionId);
        return base.OnConnectedAsync();
    }

    public override Task OnDisconnectedAsync(Exception? exception)
    {
        if (exception != null)
            _logger.LogWarning(exception, "SignalR client disconnected with error: {ConnectionId}", Context.ConnectionId);
        else
            _logger.LogInformation("SignalR client disconnected: {ConnectionId}", Context.ConnectionId);
        return base.OnDisconnectedAsync(exception);
    }

    public async Task SubscribeToBucket(string bucketId)
    {
        _logger.LogDebug("Client {ConnectionId} subscribing to bucket {BucketId}", Context.ConnectionId, bucketId);
        await Groups.AddToGroupAsync(Context.ConnectionId, $"bucket:{bucketId}");
    }

    public async Task UnsubscribeFromBucket(string bucketId)
    {
        _logger.LogDebug("Client {ConnectionId} unsubscribing from bucket {BucketId}", Context.ConnectionId, bucketId);
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"bucket:{bucketId}");
    }

    public async Task SubscribeToFile(string bucketId, string path)
    {
        _logger.LogDebug("Client {ConnectionId} subscribing to file {BucketId}/{Path}", Context.ConnectionId, bucketId, path);
        await Groups.AddToGroupAsync(Context.ConnectionId, $"file:{bucketId}:{path}");
    }

    public async Task UnsubscribeFromFile(string bucketId, string path)
    {
        _logger.LogDebug("Client {ConnectionId} unsubscribing from file {BucketId}/{Path}", Context.ConnectionId, bucketId, path);
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"file:{bucketId}:{path}");
    }

    public async Task SubscribeToAll()
    {
        var httpContext = Context.GetHttpContext();
        var token = httpContext?.Request.Query["access_token"].FirstOrDefault();

        if (string.IsNullOrEmpty(token))
        {
            _logger.LogWarning("Client {ConnectionId} attempted global subscription without token", Context.ConnectionId);
            throw new HubException("Admin authentication required for global subscriptions");
        }

        var authService = httpContext!.RequestServices.GetRequiredService<IAuthService>();
        var auth = await authService.ResolveAsync(token);

        if (!auth.IsAdmin)
        {
            _logger.LogWarning("Client {ConnectionId} attempted global subscription with non-admin token", Context.ConnectionId);
            throw new HubException("Admin authentication required for global subscriptions");
        }

        _logger.LogInformation("Client {ConnectionId} subscribed to global notifications", Context.ConnectionId);
        await Groups.AddToGroupAsync(Context.ConnectionId, "global");
    }

    public async Task UnsubscribeFromAll()
    {
        _logger.LogDebug("Client {ConnectionId} unsubscribing from global notifications", Context.ConnectionId);
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, "global");
    }
}
