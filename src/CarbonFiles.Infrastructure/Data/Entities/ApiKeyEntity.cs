namespace CarbonFiles.Infrastructure.Data.Entities;

public sealed class ApiKeyEntity
{
    public required string Prefix { get; set; }  // PK, e.g. "cf4_b259367e"
    public required string HashedSecret { get; set; }  // SHA-256 hash
    public required string Name { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? LastUsedAt { get; set; }
}
