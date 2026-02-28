namespace CarbonFiles.Core.Models;

public sealed class BucketFile
{
    public required string Path { get; init; }
    public required string Name { get; init; }
    public long Size { get; set; }
    public required string MimeType { get; init; }
    public string? ShortCode { get; set; }
    public string? ShortUrl { get; set; }
    public DateTime CreatedAt { get; init; }
    public DateTime UpdatedAt { get; set; }
}
