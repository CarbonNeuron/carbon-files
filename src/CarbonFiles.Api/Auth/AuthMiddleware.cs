using CarbonFiles.Core.Interfaces;

namespace CarbonFiles.Api.Auth;

public sealed class AuthMiddleware
{
    private readonly RequestDelegate _next;

    public AuthMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context, IAuthService authService)
    {
        var authHeader = context.Request.Headers.Authorization.FirstOrDefault();
        var token = authHeader?.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) == true
            ? authHeader["Bearer ".Length..]
            : null;

        var authContext = await authService.ResolveAsync(token);
        context.Items["AuthContext"] = authContext;

        await _next(context);
    }
}
