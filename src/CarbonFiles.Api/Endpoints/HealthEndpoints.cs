using CarbonFiles.Api.Serialization;
using CarbonFiles.Core.Models.Responses;
using CarbonFiles.Infrastructure.Data;

namespace CarbonFiles.Api.Endpoints;

public static class HealthEndpoints
{
    private static readonly DateTime StartTime = DateTime.UtcNow;

    public static void MapHealthEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/healthz", async (CarbonFilesDbContext db) =>
        {
            try
            {
                var canConnect = await db.Database.CanConnectAsync();
                if (!canConnect)
                {
                    return Results.Json(
                        new HealthResponse { Status = "unhealthy", UptimeSeconds = 0, Db = "unreachable" },
                        CarbonFilesJsonContext.Default.HealthResponse,
                        statusCode: 503);
                }

                return Results.Ok(new HealthResponse
                {
                    Status = "healthy",
                    UptimeSeconds = (long)(DateTime.UtcNow - StartTime).TotalSeconds,
                    Db = "ok"
                });
            }
            catch
            {
                return Results.Json(
                    new HealthResponse { Status = "unhealthy", UptimeSeconds = 0, Db = "error" },
                    CarbonFilesJsonContext.Default.HealthResponse,
                    statusCode: 503);
            }
        }).WithTags("Health");
    }
}
