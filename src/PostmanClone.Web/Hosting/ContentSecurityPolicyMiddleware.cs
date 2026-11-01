using Microsoft.AspNetCore.Http;
using PostmanClone.Web.Agent;

namespace PostmanClone.Web.Hosting;

/// <summary>
/// The Web's Content-Security-Policy (the kernel sends the other security headers). Scripts and styles come only from this
/// origin: the page has no inline script or style. The one script the Web loads is <c>agent-client.js</c> (ADR-0028), which is
/// same-origin. <c>connect-src</c> adds exactly the agent's loopback origins and nothing else (ADR-0028, CONTRACTS A2.6).
/// </summary>
public sealed class ContentSecurityPolicyMiddleware(RequestDelegate next, AgentWebOptions agent)
{
    private readonly string _policy = Build(agent.ConnectOrigins);

    /// <summary>The policy for the given agent origins. The only additions to <c>'self'</c> are these origins.</summary>
    public static string Build(IEnumerable<string> agentOrigins) =>
        "default-src 'self'; script-src 'self'; style-src 'self'; img-src 'self' data:; font-src 'self'; " +
        $"connect-src {string.Join(' ', ["'self'", .. agentOrigins])}; object-src 'none'; base-uri 'self'; " +
        "form-action 'self'; frame-ancestors 'none'";

    public Task InvokeAsync(HttpContext context)
    {
        // Blazor adds its own frame-ancestors header while the endpoint runs. Setting ours just before the headers
        // are sent replaces it, so the response carries one policy, not two.
        context.Response.OnStarting(() =>
        {
            context.Response.Headers["Content-Security-Policy"] = _policy;
            return Task.CompletedTask;
        });
        return next(context);
    }
}
