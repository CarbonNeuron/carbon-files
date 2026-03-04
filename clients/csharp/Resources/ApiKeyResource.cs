using CarbonFiles.Client.Internal;
using CarbonFiles.Client.Models;

namespace CarbonFiles.Client.Resources;

public class ApiKeyResource
{
    private readonly HttpTransport _transport;
    private readonly string _prefix;

    internal ApiKeyResource(HttpTransport transport, string prefix)
    {
        _transport = transport;
        _prefix = prefix;
    }

    public Task RevokeAsync(CancellationToken ct = default)
        => _transport.DeleteAsync($"/api/keys/{Uri.EscapeDataString(_prefix)}", ct);

    public Task<ApiKeyUsageResponse> GetUsageAsync(CancellationToken ct = default)
        => _transport.GetAsync<ApiKeyUsageResponse>($"/api/keys/{Uri.EscapeDataString(_prefix)}/usage", ct);
}
