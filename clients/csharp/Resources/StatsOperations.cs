using CarbonFiles.Client.Internal;
using CarbonFiles.Client.Models;

namespace CarbonFiles.Client.Resources;

public class StatsOperations
{
    private readonly HttpTransport _transport;
    internal StatsOperations(HttpTransport transport) => _transport = transport;

    public Task<StatsResponse> GetAsync(CancellationToken ct = default)
        => _transport.GetAsync<StatsResponse>("/api/stats", ct);
}
