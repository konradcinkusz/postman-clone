using Microsoft.AspNetCore.Http;

namespace PostmanClone.Web.Hosting;

/// <summary>
/// The Web's Content-Security-Policy (the kernel sends the other security headers). Scripts and styles come only
/// from this origin: the page has no inline script or style, and no JS interop (ADR-0009). Blazor's own
/// <c>blazor.web.js</c> is same-origin and the websocket is same-origin too.
/// </summary>
public sealed class ContentSecurityPolicyMiddleware(RequestDelegate next)
{
    public const string Policy =
        "default-src 'self'; script-src 'self'; style-src 'self'; img-src 'self' data:; font-src 'self'; " +
        "connect-src 'self'; object-src 'none'; base-uri 'self'; form-action 'self'; frame-ancestors 'none'";

    public Task InvokeAsync(HttpContext context)
    {
        // Blazor adds its own frame-ancestors header while the endpoint runs. Setting ours just before the headers
        // are sent replaces it, so the response carries one policy, not two.
        context.Response.OnStarting(() =>
        {
            context.Response.Headers["Content-Security-Policy"] = Policy;
            return Task.CompletedTask;
        });
        return next(context);
    }
}
