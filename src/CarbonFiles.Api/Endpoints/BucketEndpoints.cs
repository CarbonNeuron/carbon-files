using CarbonFiles.Api.Auth;
using CarbonFiles.Core.Interfaces;
using CarbonFiles.Core.Models;
using CarbonFiles.Core.Models.Requests;

namespace CarbonFiles.Api.Endpoints;

public static class BucketEndpoints
{
    public static void MapBucketEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/buckets").WithTags("Buckets");

        // POST /api/buckets — Create bucket (API key or admin, NOT public)
        group.MapPost("/", async (CreateBucketRequest request, HttpContext ctx, IBucketService svc) =>
        {
            var auth = ctx.GetAuthContext();
            if (auth.IsPublic)
                return Results.Json(new ErrorResponse { Error = "Authentication required", Hint = "Use an API key or admin key." }, statusCode: 403);

            if (string.IsNullOrWhiteSpace(request.Name))
                return Results.Json(new ErrorResponse { Error = "Name is required" }, statusCode: 400);

            try
            {
                var result = await svc.CreateAsync(request, auth);
                return Results.Created($"/api/buckets/{result.Id}", result);
            }
            catch (ArgumentException ex)
            {
                return Results.Json(new ErrorResponse { Error = ex.Message }, statusCode: 400);
            }
        });

        // GET /api/buckets — List buckets (admin sees all, API key sees own)
        group.MapGet("/", async (HttpContext ctx, IBucketService svc,
            int limit = 50, int offset = 0, string sort = "created_at", string order = "desc",
            bool include_expired = false) =>
        {
            var auth = ctx.GetAuthContext();
            if (auth.IsPublic)
                return Results.Json(new ErrorResponse { Error = "Authentication required", Hint = "Use an API key or admin key." }, statusCode: 403);

            // Only admin can include expired
            var includeExpired = include_expired && auth.IsAdmin;

            var result = await svc.ListAsync(
                new PaginationParams { Limit = limit, Offset = offset, Sort = sort, Order = order },
                auth,
                includeExpired);
            return Results.Ok(result);
        });

        // GET /api/buckets/{id} — Get bucket with files (public access)
        group.MapGet("/{id}", async (string id, IBucketService svc) =>
        {
            var result = await svc.GetByIdAsync(id);
            return result != null
                ? Results.Ok(result)
                : Results.Json(new ErrorResponse { Error = "Bucket not found" }, statusCode: 404);
        });

        // PATCH /api/buckets/{id} — Update bucket (owner or admin)
        group.MapPatch("/{id}", async (string id, UpdateBucketRequest request, HttpContext ctx, IBucketService svc) =>
        {
            var auth = ctx.GetAuthContext();
            if (auth.IsPublic)
                return Results.Json(new ErrorResponse { Error = "Authentication required", Hint = "Use an API key or admin key." }, statusCode: 403);

            // At least one field required
            if (request.Name == null && request.Description == null && request.ExpiresIn == null)
                return Results.Json(new ErrorResponse { Error = "At least one field is required" }, statusCode: 400);

            try
            {
                var result = await svc.UpdateAsync(id, request, auth);
                if (result == null)
                {
                    // Need to distinguish 404 vs 403 — check if bucket exists
                    var existing = await svc.GetByIdAsync(id);
                    if (existing == null)
                        return Results.Json(new ErrorResponse { Error = "Bucket not found" }, statusCode: 404);
                    return Results.Json(new ErrorResponse { Error = "Access denied" }, statusCode: 403);
                }
                return Results.Ok(result);
            }
            catch (ArgumentException ex)
            {
                return Results.Json(new ErrorResponse { Error = ex.Message }, statusCode: 400);
            }
        });

        // DELETE /api/buckets/{id} — Delete bucket (owner or admin)
        group.MapDelete("/{id}", async (string id, HttpContext ctx, IBucketService svc) =>
        {
            var auth = ctx.GetAuthContext();
            if (auth.IsPublic)
                return Results.Json(new ErrorResponse { Error = "Authentication required", Hint = "Use an API key or admin key." }, statusCode: 403);

            var result = await svc.DeleteAsync(id, auth);
            if (!result)
            {
                // Need to distinguish 404 vs 403 — check if bucket exists
                var existing = await svc.GetByIdAsync(id);
                if (existing == null)
                    return Results.Json(new ErrorResponse { Error = "Bucket not found" }, statusCode: 404);
                return Results.Json(new ErrorResponse { Error = "Access denied" }, statusCode: 403);
            }
            return Results.NoContent();
        });

        // GET /api/buckets/{id}/summary — Plaintext summary (public access)
        group.MapGet("/{id}/summary", async (string id, IBucketService svc) =>
        {
            var result = await svc.GetSummaryAsync(id);
            return result != null
                ? Results.Text(result, "text/plain")
                : Results.Json(new ErrorResponse { Error = "Bucket not found" }, statusCode: 404);
        });
    }
}
