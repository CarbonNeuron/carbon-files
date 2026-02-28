namespace CarbonFiles.Core.Models.Requests;

public sealed class CreateUploadTokenRequest
{
    public string? ExpiresIn { get; init; }
    public int? MaxUploads { get; init; }
}
