using CarbonFiles.Client.Internal;
using CarbonFiles.Client.Models;

namespace CarbonFiles.Client.Resources;

public class ApiKeyOperations
{
    private readonly HttpTransport _transport;
    internal ApiKeyOperations(HttpTransport transport) => _transport = transport;

    public ApiKeyResource this[string prefix] => new(_transport, prefix);

    public Task<ApiKeyResponse> CreateAsync(CreateApiKeyRequest request, CancellationToken ct = default)
        => _transport.PostAsync<CreateApiKeyRequest, ApiKeyResponse>("/api/keys", request, ct);

    public Task<PaginatedResponse<ApiKeyListItem>> ListAsync(
        PaginationOptions? pagination = null,
        CancellationToken ct = default)
    {
        var query = new Dictionary<string, string?>();
        if (pagination?.Limit != null) query["limit"] = pagination.Limit.Value.ToString();
        if (pagination?.Offset != null) query["offset"] = pagination.Offset.Value.ToString();
        if (pagination?.Sort != null) query["sort"] = pagination.Sort;
        if (pagination?.Order != null) query["order"] = pagination.Order;
        return _transport.GetAsync<PaginatedResponse<ApiKeyListItem>>("/api/keys", query, ct);
    }
}
