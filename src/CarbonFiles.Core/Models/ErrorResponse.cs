namespace CarbonFiles.Core.Models;

public sealed class ErrorResponse
{
    public required string Error { get; init; }
    public string? Hint { get; init; }
}
