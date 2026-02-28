namespace CarbonFiles.Core.Models;

public sealed class AuthContext
{
    public AuthRole Role { get; init; }
    public string? OwnerName { get; init; }
    public string? KeyPrefix { get; init; }

    public static AuthContext Admin() => new() { Role = AuthRole.Admin };
    public static AuthContext Owner(string name, string prefix) => new() { Role = AuthRole.Owner, OwnerName = name, KeyPrefix = prefix };
    public static AuthContext Public() => new() { Role = AuthRole.Public };

    public bool IsAdmin => Role == AuthRole.Admin;
    public bool IsOwner => Role == AuthRole.Owner;
    public bool IsPublic => Role == AuthRole.Public;
    public bool CanManage(string bucketOwner) => IsAdmin || (IsOwner && OwnerName == bucketOwner);
}

public enum AuthRole { Public, Owner, Admin }
