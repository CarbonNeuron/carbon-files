using CarbonFiles.Api.Auth;
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
                return Results.Json(new ErrorResponse { Error = "Admin access required", Hint = "Use the admin key or a dashboard token." }, statusCode: 403);

            if (string.IsNullOrWhiteSpace(request.Name))
                return Results.Json(new ErrorResponse { Error = "Name is required" }, statusCode: 400);

            var result = await svc.CreateAsync(request.Name);
            return Results.Created($"/api/keys/{result.Prefix}", result);
        });

        // GET /api/keys — List all API keys (Admin only, paginated)
        group.MapGet("/", async (HttpContext ctx, IApiKeyService svc,
            int limit = 50, int offset = 0, string sort = "created_at", string order = "desc") =>
        {
            var auth = ctx.GetAuthContext();
            if (!auth.IsAdmin)
                return Results.Json(new ErrorResponse { Error = "Admin access required" }, statusCode: 403);

            var result = await svc.ListAsync(new PaginationParams { Limit = limit, Offset = offset, Sort = sort, Order = order });
            return Results.Ok(result);
        });

        // DELETE /api/keys/{prefix} — Revoke API key (Admin only)
        group.MapDelete("/{prefix}", async (string prefix, HttpContext ctx, IApiKeyService svc) =>
        {
            var auth = ctx.GetAuthContext();
            if (!auth.IsAdmin)
                return Results.Json(new ErrorResponse { Error = "Admin access required" }, statusCode: 403);

            var deleted = await svc.DeleteAsync(prefix);
            return deleted ? Results.NoContent() : Results.Json(new ErrorResponse { Error = "API key not found" }, statusCode: 404);
        });

        // GET /api/keys/{prefix}/usage — Detailed key usage (Admin only)
        group.MapGet("/{prefix}/usage", async (string prefix, HttpContext ctx, IApiKeyService svc) =>
        {
            var auth = ctx.GetAuthContext();
            if (!auth.IsAdmin)
                return Results.Json(new ErrorResponse { Error = "Admin access required" }, statusCode: 403);

            var result = await svc.GetUsageAsync(prefix);
            return result != null ? Results.Ok(result) : Results.Json(new ErrorResponse { Error = "API key not found" }, statusCode: 404);
        });
    }
}
