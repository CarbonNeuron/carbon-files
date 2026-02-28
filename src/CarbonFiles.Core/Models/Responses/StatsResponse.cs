namespace CarbonFiles.Core.Models.Responses;

public sealed class StatsResponse
{
    public int TotalBuckets { get; init; }
    public int TotalFiles { get; init; }
    public long TotalSize { get; init; }
    public int TotalKeys { get; init; }
    public long TotalDownloads { get; init; }
    public required IReadOnlyList<OwnerStats> StorageByOwner { get; init; }
}

public sealed class OwnerStats
{
    public required string Owner { get; init; }
    public int BucketCount { get; init; }
    public int FileCount { get; init; }
    public long TotalSize { get; init; }
}
