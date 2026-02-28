using CarbonFiles.Core.Interfaces;
using CarbonFiles.Core.Models.Responses;
using CarbonFiles.Core.Utilities;
using CarbonFiles.Infrastructure.Auth;

namespace CarbonFiles.Infrastructure.Services;

public sealed class DashboardTokenService : IDashboardTokenService
{
    private readonly JwtHelper _jwt;

    public DashboardTokenService(JwtHelper jwt) => _jwt = jwt;

    public Task<DashboardTokenResponse> CreateAsync(string? expiresIn)
    {
        // Default to 1 hour for dashboard tokens
        var defaultExpiry = DateTime.UtcNow.AddHours(1);
        var expiresAt = ExpiryParser.Parse(expiresIn, defaultExpiry)
            ?? throw new ArgumentException("Dashboard tokens cannot use 'never' expiry");

        // Cap at 24 hours — JwtHelper.CreateDashboardToken enforces this too
        var maxExpiry = DateTime.UtcNow.AddHours(24);
        if (expiresAt > maxExpiry)
            throw new ArgumentException("Dashboard token expiry cannot exceed 24 hours");

        var (token, actualExpiry) = _jwt.CreateDashboardToken(expiresAt);

        return Task.FromResult(new DashboardTokenResponse
        {
            Token = token,
            ExpiresAt = actualExpiry
        });
    }

    public DashboardTokenInfo? ValidateToken(string token)
    {
        var (isValid, expiresAt) = _jwt.ValidateToken(token);
        if (!isValid) return null;

        return new DashboardTokenInfo
        {
            Scope = "admin",
            ExpiresAt = expiresAt
        };
    }
}
