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

        // GET|HEAD /api/buckets/{id}/files/{*filePath} — File metadata or content download
        app.MapMethods("/api/buckets/{id}/files/{*filePath}", new[] { "GET", "HEAD" },
            async (string id, string filePath, HttpContext ctx,
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

        var totalSize = stream.Length;

        ctx.Response.Headers["ETag"] = etag;
        ctx.Response.Headers["Last-Modified"] = lastModified.ToString("R");
        ctx.Response.Headers["Cache-Control"] = "public, no-cache";
        ctx.Response.Headers["Accept-Ranges"] = "bytes";

        if (ctx.Request.Query.ContainsKey("download") && ctx.Request.Query["download"] == "true")
            ctx.Response.Headers["Content-Disposition"] = $"attachment; filename=\"{meta.Name}\"";

        // Update last_used_at (fire-and-forget)
        _ = fileService.UpdateLastUsedAsync(bucketId);

        // HEAD request: return headers without body
        if (HttpMethods.IsHead(ctx.Request.Method))
        {
            await stream.DisposeAsync();
            ctx.Response.ContentLength = totalSize;
            ctx.Response.ContentType = meta.MimeType;
            return Results.Empty;
        }

        // Check for Range request
        var rangeHeader = ctx.Request.Headers.Range.FirstOrDefault();
        if (rangeHeader != null && rangeHeader.StartsWith("bytes=", StringComparison.OrdinalIgnoreCase))
        {
            // If-Range: if present, only serve partial if ETag matches; otherwise serve full
            var ifRange = ctx.Request.Headers.IfRange.FirstOrDefault();
            if (ifRange != null && ifRange != etag)
            {
                // ETag mismatch — ignore Range, serve full file
                return Results.File(stream, meta.MimeType);
            }

            var rangeSpec = rangeHeader["bytes=".Length..].Trim();
            if (TryParseRange(rangeSpec, totalSize, out var start, out var end))
            {
                var length = end - start + 1;
                ctx.Response.StatusCode = 206;
                ctx.Response.Headers["Content-Range"] = $"bytes {start}-{end}/{totalSize}";
                ctx.Response.ContentLength = length;
                ctx.Response.ContentType = meta.MimeType;

                stream.Seek(start, SeekOrigin.Begin);
                var buffer = new byte[81920];
                var remaining = length;
                while (remaining > 0)
                {
                    var toRead = (int)Math.Min(buffer.Length, remaining);
                    var read = await stream.ReadAsync(buffer.AsMemory(0, toRead));
                    if (read == 0) break;
                    await ctx.Response.Body.WriteAsync(buffer.AsMemory(0, read));
                    remaining -= read;
                }

                await stream.DisposeAsync();
                return Results.Empty;
            }
            else
            {
                // Invalid range
                await stream.DisposeAsync();
                ctx.Response.Headers["Content-Range"] = $"bytes */{totalSize}";
                return Results.StatusCode(416);
            }
        }

        return Results.File(stream, meta.MimeType);
    }

    /// <summary>
    /// Parses a single byte range spec (e.g., "0-99", "500-", "-100") against a total file size.
    /// Returns true with inclusive start/end if valid; false if the range is unsatisfiable.
    /// </summary>
    private static bool TryParseRange(string rangeSpec, long totalSize, out long start, out long end)
    {
        start = 0;
        end = 0;

        if (string.IsNullOrEmpty(rangeSpec) || totalSize == 0)
            return false;

        // Only handle single range (not multi-part ranges)
        if (rangeSpec.Contains(','))
            return false;

        var dashIndex = rangeSpec.IndexOf('-');
        if (dashIndex < 0)
            return false;

        var startPart = rangeSpec[..dashIndex].Trim();
        var endPart = rangeSpec[(dashIndex + 1)..].Trim();

        if (string.IsNullOrEmpty(startPart))
        {
            // Suffix range: "-500" means last 500 bytes
            if (!long.TryParse(endPart, out var suffixLength) || suffixLength <= 0)
                return false;

            start = Math.Max(0, totalSize - suffixLength);
            end = totalSize - 1;
            return true;
        }

        if (!long.TryParse(startPart, out start))
            return false;

        if (string.IsNullOrEmpty(endPart))
        {
            // Open-end range: "500-" means byte 500 to end
            end = totalSize - 1;
        }
        else
        {
            if (!long.TryParse(endPart, out end))
                return false;

            // Clamp end to file size
            if (end >= totalSize)
                end = totalSize - 1;
        }

        // Validate
        if (start < 0 || start >= totalSize || start > end)
            return false;

        return true;
    }
}
