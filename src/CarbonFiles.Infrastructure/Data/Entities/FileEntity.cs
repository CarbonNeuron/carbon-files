namespace CarbonFiles.Infrastructure.Data.Entities;

public sealed class FileEntity
{
    public required string BucketId { get; set; }
    public required string Path { get; set; }  // Composite PK with BucketId
    public required string Name { get; set; }
    public long Size { get; set; }
    public required string MimeType { get; set; }
    public string? ShortCode { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
