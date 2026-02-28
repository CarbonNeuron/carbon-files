using CarbonFiles.Api.Auth;
using CarbonFiles.Core.Interfaces;
using CarbonFiles.Core.Models;

namespace CarbonFiles.Api.Endpoints;

public static class ShortUrlEndpoints
{
    public static void MapShortUrlEndpoints(this IEndpointRouteBuilder app)
    {
        // GET /s/{code} — Resolve short URL (public)
        // 302 redirect to file content URL
        app.MapGet("/s/{code}", async (string code, IShortUrlService svc) =>
        {
            var url = await svc.ResolveAsync(code);
            return url != null ? Results.Redirect(url) : Results.NotFound();
        }).WithTags("Short URLs");

        // DELETE /api/short/{code} — Delete short URL (owner or admin)
        app.MapDelete("/api/short/{code}", async (string code, HttpContext ctx, IShortUrlService svc) =>
        {
            var auth = ctx.GetAuthContext();
            if (auth.IsPublic)
                return Results.Json(new ErrorResponse { Error = "Authentication required" }, statusCode: 401);

            var deleted = await svc.DeleteAsync(code, auth);
            return deleted ? Results.NoContent() : Results.NotFound();
        }).WithTags("Short URLs");
    }
}
