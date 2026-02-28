namespace CarbonFiles.Core.Models.Responses;

public sealed class ApiKeyResponse
{
    public required string Key { get; init; }
    public required string Prefix { get; init; }
    public required string Name { get; init; }
    public DateTime CreatedAt { get; init; }
}
