using CarbonFiles.Client.Internal;

namespace CarbonFiles.Client.Resources;

public class ShortUrlResource
{
    private readonly HttpTransport _transport;
    private readonly string _code;

    internal ShortUrlResource(HttpTransport transport, string code)
    {
        _transport = transport;
        _code = code;
    }

    public Task DeleteAsync(CancellationToken ct = default)
        => _transport.DeleteAsync($"/api/short/{Uri.EscapeDataString(_code)}", ct);
}
