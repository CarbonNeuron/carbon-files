using CarbonFiles.Core.Interfaces;
using CarbonFiles.Core.Models.Responses;
using CarbonFiles.Core.Utilities;
using CarbonFiles.Infrastructure.Auth;
using Microsoft.Extensions.Logging;

namespace CarbonFiles.Infrastructure.Services;

public sealed class DashboardTokenService : IDashboardTokenService
{
    private readonly JwtHelper _jwt;
    private readonly ILogger<DashboardTokenService> _logger;

    public DashboardTokenService(JwtHelper jwt, ILogger<DashboardTokenService> logger)
    {
        _jwt = jwt;
        _logger = logger;
    }

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

        _logger.LogInformation("Created dashboard token expiring at {ExpiresAt}", actualExpiry.ToString("o"));

        return Task.FromResult(new DashboardTokenResponse
        {
            Token = token,
            ExpiresAt = actualExpiry
        });
    }

    public DashboardTokenInfo? ValidateToken(string token)
    {
        var (isValid, expiresAt) = _jwt.ValidateToken(token);
        if (!isValid)
        {
            _logger.LogDebug("Dashboard token validation failed");
            return null;
        }

        return new DashboardTokenInfo
        {
            Scope = "admin",
            ExpiresAt = expiresAt
        };
    }
}
