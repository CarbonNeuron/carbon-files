namespace CarbonFiles.Infrastructure.Data.Entities;

public sealed class BucketEntity
{
    public required string Id { get; set; }
    public required string Name { get; set; }
    public required string Owner { get; set; }
    public string? OwnerKeyPrefix { get; set; }  // FK to ApiKeyEntity
    public string? Description { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public DateTime? LastUsedAt { get; set; }
    public int FileCount { get; set; }
    public long TotalSize { get; set; }
    public long DownloadCount { get; set; }
}
