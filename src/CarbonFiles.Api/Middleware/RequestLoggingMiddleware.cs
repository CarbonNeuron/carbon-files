using System.Diagnostics;

namespace CarbonFiles.Api.Middleware;

public sealed class RequestLoggingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<RequestLoggingMiddleware> _logger;

    public RequestLoggingMiddleware(RequestDelegate next, ILogger<RequestLoggingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var stopwatch = Stopwatch.StartNew();
        var method = context.Request.Method;
        var path = context.Request.Path;

        _logger.LogDebug("HTTP {Method} {Path} started", method, path);

        try
        {
            await _next(context);
        }
        finally
        {
            stopwatch.Stop();
            var statusCode = context.Response.StatusCode;
            var elapsed = stopwatch.ElapsedMilliseconds;

            var auth = context.Items["AuthContext"] as CarbonFiles.Core.Models.AuthContext;
            var tokenType = auth switch
            {
                { IsAdmin: true } => "admin",
                { IsOwner: true } => "api_key",
                _ => "public"
            };

            if (elapsed > 5000)
            {
                _logger.LogWarning("HTTP {Method} {Path} -> {StatusCode} in {ElapsedMs}ms [Auth: {TokenType}] (slow)",
                    method, path, statusCode, elapsed, tokenType);
            }
            else
            {
                _logger.LogInformation("HTTP {Method} {Path} -> {StatusCode} in {ElapsedMs}ms [Auth: {TokenType}]",
                    method, path, statusCode, elapsed, tokenType);
            }
        }
    }
}
