using CarbonFiles.Core.Models;

namespace CarbonFiles.Api.Auth;

public static class AuthExtensions
{
    public static AuthContext GetAuthContext(this HttpContext context)
        => context.Items["AuthContext"] as AuthContext ?? AuthContext.Public();
}
