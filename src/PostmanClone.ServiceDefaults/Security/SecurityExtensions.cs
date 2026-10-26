using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace PostmanClone.ServiceDefaults.Security;

/// <summary>Sends the browser-facing security headers (CONTRACTS §0). The Web adds its own Content-Security-Policy.</summary>
public sealed class SecurityHeadersMiddleware(RequestDelegate next)
{
    public Task InvokeAsync(HttpContext context)
    {
        var headers = context.Response.Headers;
        headers["X-Content-Type-Options"] = "nosniff";
        headers["Referrer-Policy"] = "no-referrer";
        headers["X-Frame-Options"] = "DENY";
        return next(context);
    }
}

public static class SecurityExtensions
{
    /// <summary>
    /// The request pipeline for a service: security headers and the origin guard first (before antiforgery and the
    /// component endpoints), then authentication, the rate limiter (it partitions by the signed-in user) and
    /// authorization. Requires <c>AddJwtAuthentication</c> and <c>AddRateLimitingDefaults</c>.
    /// </summary>
    public static WebApplication UseSecurityDefaults(this WebApplication app)
    {
        app.UseMiddleware<SecurityHeadersMiddleware>();
        app.UseMiddleware<OriginGuardMiddleware>();
        app.UseAuthentication();
        app.UseRateLimiter();
        app.UseAuthorization();
        return app;
    }
}
