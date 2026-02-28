namespace CarbonFiles.Core.Models.Responses;

public sealed class DashboardTokenInfo
{
    public required string Scope { get; init; }
    public DateTime ExpiresAt { get; init; }
}
