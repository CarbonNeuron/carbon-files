using CarbonFiles.Core.Models;

namespace CarbonFiles.Core.Interfaces;

public interface IShortUrlService
{
    Task<string> CreateAsync(string bucketId, string filePath);
    Task<string?> ResolveAsync(string code);
    Task<bool> DeleteAsync(string code, AuthContext auth);
}
