namespace CarbonFiles.Core.Models.Responses;

public sealed class ApiKeyListItem
{
    public required string Prefix { get; init; }
    public required string Name { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime? LastUsedAt { get; init; }
    public int BucketCount { get; init; }
    public int FileCount { get; init; }
    public long TotalSize { get; init; }
}
