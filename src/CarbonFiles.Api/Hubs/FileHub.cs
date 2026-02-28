using CarbonFiles.Core.Interfaces;
using Microsoft.AspNetCore.SignalR;

namespace CarbonFiles.Api.Hubs;

public class FileHub : Hub
{
    public async Task SubscribeToBucket(string bucketId)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, $"bucket:{bucketId}");
    }

    public async Task UnsubscribeFromBucket(string bucketId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"bucket:{bucketId}");
    }

    public async Task SubscribeToFile(string bucketId, string path)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, $"file:{bucketId}:{path}");
    }

    public async Task UnsubscribeFromFile(string bucketId, string path)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"file:{bucketId}:{path}");
    }

    public async Task SubscribeToAll()
    {
        // Verify admin auth from query string token
        var httpContext = Context.GetHttpContext();
        var token = httpContext?.Request.Query["access_token"].FirstOrDefault();

        if (string.IsNullOrEmpty(token))
            throw new HubException("Admin authentication required for global subscriptions");

        var authService = httpContext!.RequestServices.GetRequiredService<IAuthService>();
        var auth = await authService.ResolveAsync(token);

        if (!auth.IsAdmin)
            throw new HubException("Admin authentication required for global subscriptions");

        await Groups.AddToGroupAsync(Context.ConnectionId, "global");
    }

    public async Task UnsubscribeFromAll()
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, "global");
    }
}
