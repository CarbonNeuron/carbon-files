using CarbonFiles.Api.Auth;
using CarbonFiles.Api.Serialization;
using CarbonFiles.Core.Interfaces;
using CarbonFiles.Core.Models;
using CarbonFiles.Core.Models.Requests;

namespace CarbonFiles.Api.Endpoints;

public static class KeyEndpoints
{
    public static void MapKeyEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/keys").WithTags("API Keys");

        // POST /api/keys — Create API key (Admin only)
        group.MapPost("/", async (CreateApiKeyRequest request, HttpContext ctx, IApiKeyService svc) =>
        {
            var auth = ctx.GetAuthContext();
            if (!auth.IsAdmin)
                return Results.Json(new ErrorResponse { Error = "Admin access required", Hint = "Use the admin key or a dashboard token." }, CarbonFilesJsonContext.Default.ErrorResponse, statusCode: 403);

            if (string.IsNullOrWhiteSpace(request.Name))
                return Results.Json(new ErrorResponse { Error = "Name is required" }, CarbonFilesJsonContext.Default.ErrorResponse, statusCode: 400);

            var result = await svc.CreateAsync(request.Name);
            return Results.Created($"/api/keys/{result.Prefix}", result);
        })
        .WithSummary("Create API key")
        .WithDescription("Auth: Admin only. Creates a new API key scoped to its own buckets. Returns the full key (only shown once).");

        // GET /api/keys — List all API keys (Admin only, paginated)
        group.MapGet("/", async (HttpContext ctx, IApiKeyService svc,
            int limit = 50, int offset = 0, string sort = "created_at", string order = "desc") =>
        {
            var auth = ctx.GetAuthContext();
            if (!auth.IsAdmin)
                return Results.Json(new ErrorResponse { Error = "Admin access required" }, CarbonFilesJsonContext.Default.ErrorResponse, statusCode: 403);

            var result = await svc.ListAsync(new PaginationParams { Limit = limit, Offset = offset, Sort = sort, Order = order });
            return Results.Ok(result);
        })
        .WithSummary("List API keys")
        .WithDescription("Auth: Admin only. Returns a paginated list of all API keys (secrets are masked).");

        // DELETE /api/keys/{prefix} — Revoke API key (Admin only)
        group.MapDelete("/{prefix}", async (string prefix, HttpContext ctx, IApiKeyService svc) =>
        {
            var auth = ctx.GetAuthContext();
            if (!auth.IsAdmin)
                return Results.Json(new ErrorResponse { Error = "Admin access required" }, CarbonFilesJsonContext.Default.ErrorResponse, statusCode: 403);

            var deleted = await svc.DeleteAsync(prefix);
            return deleted ? Results.NoContent() : Results.Json(new ErrorResponse { Error = "API key not found" }, CarbonFilesJsonContext.Default.ErrorResponse, statusCode: 404);
        })
        .WithSummary("Revoke API key")
        .WithDescription("Auth: Admin only. Permanently revokes an API key by its prefix.");

        // GET /api/keys/{prefix}/usage — Detailed key usage (Admin only)
        group.MapGet("/{prefix}/usage", async (string prefix, HttpContext ctx, IApiKeyService svc) =>
        {
            var auth = ctx.GetAuthContext();
            if (!auth.IsAdmin)
                return Results.Json(new ErrorResponse { Error = "Admin access required" }, CarbonFilesJsonContext.Default.ErrorResponse, statusCode: 403);

            var result = await svc.GetUsageAsync(prefix);
            return result != null ? Results.Ok(result) : Results.Json(new ErrorResponse { Error = "API key not found" }, CarbonFilesJsonContext.Default.ErrorResponse, statusCode: 404);
        })
        .WithSummary("Get API key usage")
        .WithDescription("Auth: Admin only. Returns detailed usage statistics for an API key (bucket count, file count, total size).");
    }
}
