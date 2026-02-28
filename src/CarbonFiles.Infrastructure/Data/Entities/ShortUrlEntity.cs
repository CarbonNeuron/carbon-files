namespace CarbonFiles.Infrastructure.Data.Entities;

public sealed class ShortUrlEntity
{
    public required string Code { get; set; }  // PK, 6-char
    public required string BucketId { get; set; }
    public required string FilePath { get; set; }
    public DateTime CreatedAt { get; set; }
}
