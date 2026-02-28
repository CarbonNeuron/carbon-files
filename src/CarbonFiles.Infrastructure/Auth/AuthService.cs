using CarbonFiles.Core.Interfaces;
using CarbonFiles.Core.Models;
using CarbonFiles.Core.Configuration;
using CarbonFiles.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace CarbonFiles.Infrastructure.Auth;

public sealed class AuthService : IAuthService
{
    private readonly CarbonFilesDbContext _db;
    private readonly CarbonFilesOptions _options;
    private readonly JwtHelper _jwt;
    private readonly IMemoryCache _cache;
    private static readonly TimeSpan CacheDuration = TimeSpan.FromSeconds(30);

    public AuthService(CarbonFilesDbContext db, IOptions<CarbonFilesOptions> options, JwtHelper jwt, IMemoryCache cache)
    {
        _db = db;
        _options = options.Value;
        _jwt = jwt;
        _cache = cache;
    }

    public async Task<AuthContext> ResolveAsync(string? bearerToken)
    {
        if (string.IsNullOrEmpty(bearerToken))
            return AuthContext.Public();

        // 1. Check admin key
        if (bearerToken == _options.AdminKey)
            return AuthContext.Admin();

        // 2. Check API key (cf4_ prefix)
        if (bearerToken.StartsWith("cf4_"))
        {
            var cacheKey = $"apikey:{bearerToken}";
            if (_cache.TryGetValue(cacheKey, out (string Name, string Prefix) cached))
                return AuthContext.Owner(cached.Name, cached.Prefix);

            var result = await ValidateApiKeyAsync(bearerToken);
            if (result != null)
            {
                _cache.Set(cacheKey, result.Value, CacheDuration);
                return AuthContext.Owner(result.Value.Name, result.Value.Prefix);
            }
            return AuthContext.Public(); // Invalid API key
        }

        // 3. Check dashboard JWT
        var (isValid, _) = _jwt.ValidateToken(bearerToken);
        if (isValid)
            return AuthContext.Admin();

        return AuthContext.Public();
    }

    private async Task<(string Name, string Prefix)?> ValidateApiKeyAsync(string fullKey)
    {
        // cf4_{8hex}_{32hex}
        var parts = fullKey.Split('_', 3);
        if (parts.Length != 3 || parts[0] != "cf4") return null;

        var prefix = $"cf4_{parts[1]}";
        var secret = parts[2];

        var entity = await _db.ApiKeys.FirstOrDefaultAsync(k => k.Prefix == prefix);
        if (entity == null) return null;

        var hashed = Convert.ToHexStringLower(
            System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes(secret)));

        if (hashed != entity.HashedSecret) return null;

        // Update last_used_at
        entity.LastUsedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        return (entity.Name, entity.Prefix);
    }
}
