namespace CarbonFiles.Core.Models.Responses;

public sealed class HealthResponse
{
    public required string Status { get; init; }
    public long UptimeSeconds { get; init; }
    public required string Db { get; init; }
}
