using CarbonFiles.Core.Models;

namespace CarbonFiles.Core.Interfaces;

public interface IAuthService
{
    Task<AuthContext> ResolveAsync(string? bearerToken);
}
