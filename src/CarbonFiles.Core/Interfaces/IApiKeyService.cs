using CarbonFiles.Core.Models;
using CarbonFiles.Core.Models.Responses;

namespace CarbonFiles.Core.Interfaces;

public interface IApiKeyService
{
    Task<ApiKeyResponse> CreateAsync(string name);
    Task<PaginatedResponse<ApiKeyListItem>> ListAsync(PaginationParams pagination);
    Task<bool> DeleteAsync(string prefix);
    Task<ApiKeyUsageResponse?> GetUsageAsync(string prefix);
    Task<(string Name, string Prefix)?> ValidateKeyAsync(string fullKey);
}
