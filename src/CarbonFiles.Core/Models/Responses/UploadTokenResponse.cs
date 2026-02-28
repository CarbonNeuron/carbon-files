namespace CarbonFiles.Core.Models.Responses;

public sealed class UploadTokenResponse
{
    public required string Token { get; init; }
    public required string BucketId { get; init; }
    public DateTime ExpiresAt { get; init; }
    public int? MaxUploads { get; init; }
    public int UploadsUsed { get; init; }
}
