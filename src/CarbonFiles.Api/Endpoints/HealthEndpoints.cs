using CarbonFiles.Api.Serialization;
using CarbonFiles.Core.Models.Responses;
using CarbonFiles.Infrastructure.Data;

namespace CarbonFiles.Api.Endpoints;

public static class HealthEndpoints
{
    private static readonly DateTime StartTime = DateTime.UtcNow;

    public static void MapHealthEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/healthz", async (CarbonFilesDbContext db, ILoggerFactory loggerFactory) =>
        {
            var logger = loggerFactory.CreateLogger("CarbonFiles.Api.Endpoints.HealthEndpoints");
            try
            {
                var canConnect = await db.Database.CanConnectAsync();
                if (!canConnect)
                {
                    logger.LogWarning("Health check failed: database {Status}", "unreachable");
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
                logger.LogWarning("Health check failed: database {Status}", "error");
                return Results.Json(
                    new HealthResponse { Status = "unhealthy", UptimeSeconds = 0, Db = "error" },
                    CarbonFilesJsonContext.Default.HealthResponse,
                    statusCode: 503);
            }
        })
        .Produces<HealthResponse>(200)
        .Produces<HealthResponse>(503)
        .WithTags("Health")
        .WithSummary("Health check")
        .WithDescription("Public. Returns API health status including uptime and database connectivity.");
    }
}
