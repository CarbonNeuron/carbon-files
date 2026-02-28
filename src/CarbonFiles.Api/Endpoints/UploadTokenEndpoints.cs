using CarbonFiles.Api.Auth;
using CarbonFiles.Core.Interfaces;
using CarbonFiles.Core.Models;
using CarbonFiles.Core.Models.Requests;

namespace CarbonFiles.Api.Endpoints;

public static class UploadTokenEndpoints
{
    public static void MapUploadTokenEndpoints(this IEndpointRouteBuilder app)
    {
        // POST /api/buckets/{id}/tokens — Create upload token (owner or admin)
        app.MapPost("/api/buckets/{id}/tokens", async (string id, CreateUploadTokenRequest? request, HttpContext ctx, IUploadTokenService svc) =>
        {
            var auth = ctx.GetAuthContext();
            if (auth.IsPublic)
                return Results.Json(new ErrorResponse { Error = "Authentication required", Hint = "Use an API key or admin key." }, statusCode: 403);

            try
            {
                var result = await svc.CreateAsync(id, request ?? new(), auth);
                if (result == null)
                {
                    return Results.Json(new ErrorResponse { Error = "Bucket not found or access denied" }, statusCode: 404);
                }
                return Results.Created($"/api/buckets/{id}/tokens", result);
            }
            catch (ArgumentException ex)
            {
                return Results.Json(new ErrorResponse { Error = ex.Message }, statusCode: 400);
            }
        })
        .WithTags("Upload Tokens")
        .WithSummary("Create upload token")
        .WithDescription("Auth: Bucket owner or admin. Creates a scoped upload token for a specific bucket with optional expiry and upload limit.");
    }
}
