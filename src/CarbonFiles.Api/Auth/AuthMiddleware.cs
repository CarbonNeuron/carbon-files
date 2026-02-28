using CarbonFiles.Core.Interfaces;

namespace CarbonFiles.Api.Auth;

public sealed class AuthMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<AuthMiddleware> _logger;

    public AuthMiddleware(RequestDelegate next, ILogger<AuthMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context, IAuthService authService)
    {
        var authHeader = context.Request.Headers.Authorization.FirstOrDefault();
        var token = authHeader?.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) == true
            ? authHeader["Bearer ".Length..]
            : null;

        _logger.LogDebug("Resolving auth for {Method} {Path}", context.Request.Method, context.Request.Path);

        var authContext = await authService.ResolveAsync(token);
        context.Items["AuthContext"] = authContext;

        if (token != null && authContext.IsPublic)
        {
            _logger.LogWarning("Auth failed for {Method} {Path} — invalid token provided", context.Request.Method, context.Request.Path);
        }
        else if (!authContext.IsPublic)
        {
            var tokenType = authContext.IsAdmin ? "admin" : "api_key";
            _logger.LogDebug("Auth resolved as {TokenType} for {Method} {Path}", tokenType, context.Request.Method, context.Request.Path);
        }

        await _next(context);
    }
}
