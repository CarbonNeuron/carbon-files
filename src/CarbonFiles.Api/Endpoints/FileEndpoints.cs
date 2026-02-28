using CarbonFiles.Api.Auth;
using CarbonFiles.Core.Interfaces;
using CarbonFiles.Core.Models;
using CarbonFiles.Infrastructure.Services;

namespace CarbonFiles.Api.Endpoints;

public static class FileEndpoints
{
    public static void MapFileEndpoints(this IEndpointRouteBuilder app)
    {
        // GET /api/buckets/{id}/files — List files (public, paginated)
        app.MapGet("/api/buckets/{id}/files", async (string id, IFileService fileService, IBucketService bucketService,
            int limit = 50, int offset = 0, string sort = "created_at", string order = "desc") =>
        {
            // Check bucket exists
            var bucket = await bucketService.GetByIdAsync(id);
            if (bucket == null)
                return Results.Json(new ErrorResponse { Error = "Bucket not found" }, statusCode: 404);

            var result = await fileService.ListAsync(id,
                new PaginationParams { Limit = limit, Offset = offset, Sort = sort, Order = order });
            return Results.Ok(result);
        });

        // GET /api/buckets/{id}/files/{*filePath} — File metadata or content download
        app.MapGet("/api/buckets/{id}/files/{*filePath}", async (string id, string filePath, HttpContext ctx,
            IFileService fileService, FileStorageService storageService, IBucketService bucketService) =>
        {
            // Check bucket exists
            var bucket = await bucketService.GetByIdAsync(id);
            if (bucket == null)
                return Results.Json(new ErrorResponse { Error = "Bucket not found" }, statusCode: 404);

            if (filePath.EndsWith("/content", StringComparison.OrdinalIgnoreCase))
            {
                var actualPath = filePath[..^"/content".Length];
                return await ServeFileContent(id, actualPath, ctx, fileService, storageService);
            }

            // Return file metadata
            var meta = await fileService.GetMetadataAsync(id, filePath);
            return meta != null
                ? Results.Ok(meta)
                : Results.Json(new ErrorResponse { Error = "File not found" }, statusCode: 404);
        });

        // DELETE /api/buckets/{id}/files/{*filePath} — Delete file (owner or admin)
        app.MapDelete("/api/buckets/{id}/files/{*filePath}", async (string id, string filePath, HttpContext ctx,
            IFileService fileService, IBucketService bucketService) =>
        {
            // Check bucket exists
            var bucket = await bucketService.GetByIdAsync(id);
            if (bucket == null)
                return Results.Json(new ErrorResponse { Error = "Bucket not found" }, statusCode: 404);

            var auth = ctx.GetAuthContext();
            if (auth.IsPublic)
                return Results.Json(new ErrorResponse { Error = "Authentication required", Hint = "Use an API key or admin key." }, statusCode: 403);

            var deleted = await fileService.DeleteAsync(id, filePath, auth);
            return deleted ? Results.NoContent() : Results.Json(new ErrorResponse { Error = "File not found" }, statusCode: 404);
        });
    }

    private static async Task<IResult> ServeFileContent(string bucketId, string path, HttpContext ctx,
        IFileService fileService, FileStorageService storageService)
    {
        var meta = await fileService.GetMetadataAsync(bucketId, path);
        if (meta == null)
            return Results.Json(new ErrorResponse { Error = "File not found" }, statusCode: 404);

        var etag = $"\"{meta.Size}-{meta.UpdatedAt.Ticks}\"";
        var lastModified = meta.UpdatedAt;

        // Conditional request: If-None-Match
        if (ctx.Request.Headers.IfNoneMatch.FirstOrDefault() == etag)
            return Results.StatusCode(304);

        // Conditional request: If-Modified-Since
        if (ctx.Request.Headers.IfModifiedSince.Count > 0)
        {
            if (DateTimeOffset.TryParse(ctx.Request.Headers.IfModifiedSince, out var ifModifiedSince))
            {
                if (lastModified <= ifModifiedSince.UtcDateTime.AddSeconds(1))
                    return Results.StatusCode(304);
            }
        }

        var stream = storageService.OpenRead(bucketId, path);
        if (stream == null)
            return Results.Json(new ErrorResponse { Error = "File not found" }, statusCode: 404);

        ctx.Response.Headers["ETag"] = etag;
        ctx.Response.Headers["Last-Modified"] = lastModified.ToString("R");
        ctx.Response.Headers["Cache-Control"] = "public, no-cache";
        ctx.Response.Headers["Accept-Ranges"] = "bytes";

        if (ctx.Request.Query.ContainsKey("download") && ctx.Request.Query["download"] == "true")
            ctx.Response.Headers["Content-Disposition"] = $"attachment; filename=\"{meta.Name}\"";

        // Update last_used_at (fire-and-forget)
        _ = fileService.UpdateLastUsedAsync(bucketId);

        return Results.File(stream, meta.MimeType, enableRangeProcessing: true);
    }
}
