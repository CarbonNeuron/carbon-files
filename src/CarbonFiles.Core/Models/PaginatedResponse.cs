namespace CarbonFiles.Core.Models;

public sealed class PaginatedResponse<T>
{
    public required IReadOnlyList<T> Items { get; init; }
    public int Total { get; init; }
    public int Limit { get; init; }
    public int Offset { get; init; }
}
