namespace CarbonFiles.Core.Models.Responses;

public sealed class DashboardTokenResponse
{
    public required string Token { get; init; }
    public DateTime ExpiresAt { get; init; }
}
