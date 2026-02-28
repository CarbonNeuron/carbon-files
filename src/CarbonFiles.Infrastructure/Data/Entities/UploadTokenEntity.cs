namespace CarbonFiles.Infrastructure.Data.Entities;

public sealed class UploadTokenEntity
{
    public required string Token { get; set; }  // PK, "cfu_..."
    public required string BucketId { get; set; }
    public DateTime ExpiresAt { get; set; }
    public int? MaxUploads { get; set; }
    public int UploadsUsed { get; set; }
    public DateTime CreatedAt { get; set; }
}
