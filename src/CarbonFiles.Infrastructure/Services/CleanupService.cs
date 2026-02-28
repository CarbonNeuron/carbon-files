using CarbonFiles.Core.Configuration;
using CarbonFiles.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CarbonFiles.Infrastructure.Services;

public sealed class CleanupService : BackgroundService
{
    private readonly IServiceProvider _provider;
    private readonly int _intervalMinutes;
    private readonly ILogger<CleanupService> _logger;

    public CleanupService(IServiceProvider provider, IOptions<CarbonFilesOptions> options, ILogger<CleanupService> logger)
    {
        _provider = provider;
        _intervalMinutes = options.Value.CleanupIntervalMinutes;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Wait a bit before first cleanup
        await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await CleanupExpiredBucketsAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during cleanup");
            }

            await Task.Delay(TimeSpan.FromMinutes(_intervalMinutes), stoppingToken);
        }
    }

    internal async Task CleanupExpiredBucketsAsync(CancellationToken ct)
    {
        using var scope = _provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CarbonFilesDbContext>();
        var storage = scope.ServiceProvider.GetRequiredService<FileStorageService>();

        var now = DateTime.UtcNow;
        var expired = await db.Buckets
            .Where(b => b.ExpiresAt != null && b.ExpiresAt < now)
            .ToListAsync(ct);

        if (expired.Count == 0) return;

        _logger.LogInformation("Cleaning up {Count} expired buckets", expired.Count);

        foreach (var bucket in expired)
        {
            // Delete files from disk
            storage.DeleteBucketDir(bucket.Id);

            // Delete associated DB records
            await db.Files.Where(f => f.BucketId == bucket.Id).ExecuteDeleteAsync(ct);
            await db.ShortUrls.Where(s => s.BucketId == bucket.Id).ExecuteDeleteAsync(ct);
            await db.UploadTokens.Where(t => t.BucketId == bucket.Id).ExecuteDeleteAsync(ct);
            db.Buckets.Remove(bucket);
        }

        await db.SaveChangesAsync(ct);
        _logger.LogInformation("Cleaned up {Count} expired buckets", expired.Count);
    }
}
