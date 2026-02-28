using CarbonFiles.Api.Auth;
using CarbonFiles.Api.Serialization;
using CarbonFiles.Core.Models;
using CarbonFiles.Core.Models.Responses;
using CarbonFiles.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace CarbonFiles.Api.Endpoints;

public static class StatsEndpoints
{
    public static void MapStatsEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/stats", async (HttpContext ctx, CarbonFilesDbContext db) =>
        {
            var auth = ctx.GetAuthContext();
            if (!auth.IsAdmin)
                return Results.Json(new ErrorResponse { Error = "Admin access required" }, CarbonFilesJsonContext.Default.ErrorResponse, statusCode: 403);

            var now = DateTime.UtcNow;
            var activeBuckets = db.Buckets.Where(b => b.ExpiresAt == null || b.ExpiresAt > now);

            var stats = new StatsResponse
            {
                TotalBuckets = await activeBuckets.CountAsync(),
                TotalFiles = await db.Files.CountAsync(),
                TotalSize = await db.Files.SumAsync(f => (long?)f.Size) ?? 0,
                TotalKeys = await db.ApiKeys.CountAsync(),
                TotalDownloads = await activeBuckets.SumAsync(b => (long?)b.DownloadCount) ?? 0,
                StorageByOwner = await activeBuckets
                    .GroupBy(b => b.Owner)
                    .Select(g => new OwnerStats
                    {
                        Owner = g.Key,
                        BucketCount = g.Count(),
                        FileCount = g.Sum(b => b.FileCount),
                        TotalSize = g.Sum(b => b.TotalSize)
                    }).ToListAsync()
            };

            return Results.Ok(stats);
        })
        .WithTags("Stats")
        .WithSummary("Get system statistics")
        .WithDescription("Auth: Admin only. Returns system-wide statistics including total buckets, files, storage, and per-owner breakdowns.");
    }
}
