using CarbonFiles.Api.Auth;
using CarbonFiles.Core.Interfaces;
using CarbonFiles.Core.Models;
using CarbonFiles.Core.Models.Requests;

namespace CarbonFiles.Api.Endpoints;

public static class TokenEndpoints
{
    public static void MapTokenEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/tokens/dashboard").WithTags("Dashboard Tokens");

        // POST /api/tokens/dashboard — Create dashboard token (Admin only)
        group.MapPost("/", async (CreateDashboardTokenRequest? request, HttpContext ctx, IDashboardTokenService svc) =>
        {
            var auth = ctx.GetAuthContext();
            if (!auth.IsAdmin)
                return Results.Json(new ErrorResponse { Error = "Admin access required" }, statusCode: 403);

            try
            {
                var result = await svc.CreateAsync(request?.ExpiresIn);
                return Results.Created("/api/tokens/dashboard/me", result);
            }
            catch (ArgumentException ex)
            {
                return Results.Json(new ErrorResponse { Error = ex.Message }, statusCode: 400);
            }
        })
        .WithSummary("Create dashboard token")
        .WithDescription("Auth: Admin only. Creates a short-lived JWT token for dashboard access with optional custom expiry.");

        // GET /api/tokens/dashboard/me — Validate current token
        group.MapGet("/me", (HttpContext ctx, IDashboardTokenService svc) =>
        {
            // This endpoint is specifically for dashboard tokens
            var authHeader = ctx.Request.Headers.Authorization.FirstOrDefault();
            var token = authHeader?.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) == true
                ? authHeader["Bearer ".Length..]
                : null;

            if (token == null)
                return Results.Json(new ErrorResponse { Error = "No token provided" }, statusCode: 401);

            var info = svc.ValidateToken(token);
            return info != null ? Results.Ok(info) : Results.Json(new ErrorResponse { Error = "Invalid or expired token" }, statusCode: 401);
        })
        .WithSummary("Validate dashboard token")
        .WithDescription("Auth: Dashboard token (Bearer). Validates the current dashboard token and returns its metadata (expiry, issued at).");
    }
}
