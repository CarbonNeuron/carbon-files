using CarbonFiles.Api.Auth;
using CarbonFiles.Api.Serialization;
using CarbonFiles.Core.Configuration;
using CarbonFiles.Core.Interfaces;
using CarbonFiles.Core.Models;
using CarbonFiles.Core.Models.Responses;
using Microsoft.Extensions.Options;

namespace CarbonFiles.Api.Endpoints;

public static class UploadEndpoints
{
    private static readonly HashSet<string> GenericNames = new(StringComparer.OrdinalIgnoreCase)
        { "file", "files", "upload", "uploads", "blob" };

    public static void MapUploadEndpoints(this IEndpointRouteBuilder app)
    {
        // POST /api/buckets/{id}/upload — Multipart upload
        app.MapPost("/api/buckets/{id}/upload", async (string id, HttpContext ctx,
            IUploadService uploadService, IBucketService bucketService,
            IUploadTokenService uploadTokenService, IOptions<CarbonFilesOptions> options) =>
        {
            // Check bucket exists
            var bucket = await bucketService.GetByIdAsync(id);
            if (bucket == null)
                return Results.Json(new ErrorResponse { Error = "Bucket not found" }, CarbonFilesJsonContext.Default.ErrorResponse, statusCode: 404);

            // Auth check: owner, admin, or upload token
            var auth = ctx.GetAuthContext();
            string? validatedToken = null;
            if (auth.IsPublic)
            {
                var token = ctx.Request.Query["token"].FirstOrDefault();
                if (string.IsNullOrEmpty(token))
                    return Results.Json(new ErrorResponse { Error = "Authentication required", Hint = "Use an API key, admin key, or upload token." }, CarbonFilesJsonContext.Default.ErrorResponse, statusCode: 403);

                // Validate upload token via service
                var (tokenBucketId, isValid) = await uploadTokenService.ValidateAsync(token);
                if (!isValid || tokenBucketId != id)
                    return Results.Json(new ErrorResponse { Error = "Invalid or expired upload token" }, CarbonFilesJsonContext.Default.ErrorResponse, statusCode: 403);

                validatedToken = token;
                // Use admin auth context for upload token (it's authorized)
                auth = AuthContext.Admin();
            }

            if (!ctx.Request.HasFormContentType)
                return Results.Json(new ErrorResponse { Error = "Expected multipart/form-data" }, CarbonFilesJsonContext.Default.ErrorResponse, statusCode: 400);

            var form = await ctx.Request.ReadFormAsync();
            var uploaded = new List<BucketFile>();
            var maxUploadSize = options.Value.MaxUploadSize;

            foreach (var file in form.Files)
            {
                // Determine path
                var path = GenericNames.Contains(file.Name) ? file.FileName : file.Name;

                if (string.IsNullOrWhiteSpace(path))
                    return Results.Json(new ErrorResponse { Error = "File path could not be determined" }, CarbonFilesJsonContext.Default.ErrorResponse, statusCode: 400);

                // Check max upload size
                if (maxUploadSize > 0 && file.Length > maxUploadSize)
                    return Results.Json(new ErrorResponse { Error = "File too large" }, CarbonFilesJsonContext.Default.ErrorResponse, statusCode: 413);

                await using var stream = file.OpenReadStream();
                var result = await uploadService.StoreFileAsync(id, path, stream, auth);
                uploaded.Add(result);
            }

            // Update upload token usage if applicable
            if (validatedToken != null && uploaded.Count > 0)
            {
                await uploadTokenService.IncrementUsageAsync(validatedToken, uploaded.Count);
            }

            return Results.Created($"/api/buckets/{id}/files", new UploadResponse { Uploaded = uploaded });
        })
        .DisableAntiforgery()
        .WithTags("Uploads")
        .WithSummary("Upload files (multipart)")
        .WithDescription("Auth: Bucket owner, admin, or upload token (?token=). Upload one or more files via multipart/form-data. Field names become file paths unless generic (file, files, upload, etc.).");

        // PUT /api/buckets/{id}/upload/stream — Stream upload (single file)
        app.MapPut("/api/buckets/{id}/upload/stream", async (string id, HttpContext ctx,
            IUploadService uploadService, IBucketService bucketService,
            IUploadTokenService uploadTokenService) =>
        {
            // Check bucket exists
            var bucket = await bucketService.GetByIdAsync(id);
            if (bucket == null)
                return Results.Json(new ErrorResponse { Error = "Bucket not found" }, CarbonFilesJsonContext.Default.ErrorResponse, statusCode: 404);

            // Auth check: owner, admin, or upload token
            var auth = ctx.GetAuthContext();
            string? validatedToken = null;
            if (auth.IsPublic)
            {
                var token = ctx.Request.Query["token"].FirstOrDefault();
                if (string.IsNullOrEmpty(token))
                    return Results.Json(new ErrorResponse { Error = "Authentication required", Hint = "Use an API key, admin key, or upload token." }, CarbonFilesJsonContext.Default.ErrorResponse, statusCode: 403);

                // Validate upload token via service
                var (tokenBucketId, isValid) = await uploadTokenService.ValidateAsync(token);
                if (!isValid || tokenBucketId != id)
                    return Results.Json(new ErrorResponse { Error = "Invalid or expired upload token" }, CarbonFilesJsonContext.Default.ErrorResponse, statusCode: 403);

                validatedToken = token;
                auth = AuthContext.Admin();
            }

            var filename = ctx.Request.Query["filename"].FirstOrDefault();
            if (string.IsNullOrEmpty(filename))
                return Results.Json(new ErrorResponse { Error = "filename query parameter is required" }, CarbonFilesJsonContext.Default.ErrorResponse, statusCode: 400);

            var result = await uploadService.StoreFileAsync(id, filename, ctx.Request.Body, auth);

            // Update upload token usage if applicable
            if (validatedToken != null)
            {
                await uploadTokenService.IncrementUsageAsync(validatedToken, 1);
            }

            return Results.Created($"/api/buckets/{id}/files/{result.Path}", new UploadResponse { Uploaded = [result] });
        })
        .WithTags("Uploads")
        .WithSummary("Upload file (streaming)")
        .WithDescription("Auth: Bucket owner, admin, or upload token (?token=). Stream-upload a single file. Requires ?filename= query parameter.");
    }
}
