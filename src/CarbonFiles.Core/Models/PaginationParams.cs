namespace CarbonFiles.Core.Models;

public sealed class PaginationParams
{
    public int Limit { get; init; } = 50;
    public int Offset { get; init; } = 0;
    public string Sort { get; init; } = "created_at";
    public string Order { get; init; } = "desc";
}
