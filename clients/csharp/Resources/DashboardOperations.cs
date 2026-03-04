using CarbonFiles.Client.Internal;
using CarbonFiles.Client.Models;

namespace CarbonFiles.Client.Resources;

public class DashboardOperations
{
    private readonly HttpTransport _transport;
    internal DashboardOperations(HttpTransport transport) => _transport = transport;

    public Task<DashboardTokenResponse> CreateTokenAsync(CreateDashboardTokenRequest? request = null, CancellationToken ct = default)
        => _transport.PostAsync<CreateDashboardTokenRequest?, DashboardTokenResponse>("/api/tokens/dashboard", request, ct);

    public Task<DashboardTokenInfo> GetCurrentUserAsync(CancellationToken ct = default)
        => _transport.GetAsync<DashboardTokenInfo>("/api/tokens/dashboard/me", ct);
}
