using CarbonFiles.Client.Internal;
using CarbonFiles.Client.Models;

namespace CarbonFiles.Client.Resources;

public class UploadTokenOperations
{
    private readonly HttpTransport _transport;
    private readonly string _bucketId;

    internal UploadTokenOperations(HttpTransport transport, string bucketId)
    {
        _transport = transport;
        _bucketId = bucketId;
    }

    public Task<UploadTokenResponse> CreateAsync(CreateUploadTokenRequest request, CancellationToken ct = default)
        => _transport.PostAsync<CreateUploadTokenRequest, UploadTokenResponse>(
            $"/api/buckets/{Uri.EscapeDataString(_bucketId)}/tokens", request, ct);
}
