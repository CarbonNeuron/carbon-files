namespace CarbonFiles.Client.Models;

public class PaginationOptions
{
    public int? Limit { get; set; }
    public int? Offset { get; set; }
    public string? Sort { get; set; }
    public string? Order { get; set; }
}
