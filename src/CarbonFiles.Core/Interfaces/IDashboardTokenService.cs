using CarbonFiles.Core.Models.Responses;

namespace CarbonFiles.Core.Interfaces;

public interface IDashboardTokenService
{
    Task<DashboardTokenResponse> CreateAsync(string? expiresIn);
    DashboardTokenInfo? ValidateToken(string token);
}
