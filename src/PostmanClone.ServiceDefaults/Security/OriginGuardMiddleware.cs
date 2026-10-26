using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;

namespace PostmanClone.ServiceDefaults.Security;

/// <summary>
/// Refuses requests a page on another site could make through the user's browser (BLAZOR-WEB-APP §8): an
/// <c>Origin</c> that is neither this host nor listed in <c>AllowedOrigins</c>, and a <c>Sec-Fetch-Site: cross-site</c>
/// request other than a top-level GET/HEAD navigation. A request with no <c>Origin</c> (curl, the service's own
/// HttpClient, health probes) is allowed. This is a mitigation, not authentication, and it grants no CORS.
/// Behind a reverse proxy, configure forwarded headers before this middleware so the host is the public one.
/// </summary>
public sealed class OriginGuardMiddleware(RequestDelegate next, IConfiguration configuration)
{
    private readonly HashSet<string> _allowedOrigins = new(
        configuration.GetSection("AllowedOrigins").Get<string[]>() ?? [],
        StringComparer.OrdinalIgnoreCase);

    public async Task InvokeAsync(HttpContext context)
    {
        if (IsAllowed(context.Request))
        {
            await next(context);
            return;
        }

        await Results.Problem(
            title: "Requests from another site are refused.",
            statusCode: StatusCodes.Status403Forbidden).ExecuteAsync(context);
    }

    private bool IsAllowed(HttpRequest request)
    {
        if (request.Headers.TryGetValue("Origin", out var origin)
            && !IsOwnOrConfigured(request, origin.ToString()))
        {
            return false;
        }

        var crossSite = request.Headers.TryGetValue("Sec-Fetch-Site", out var site)
            && string.Equals(site.ToString(), "cross-site", StringComparison.OrdinalIgnoreCase);
        return !crossSite || IsTopLevelNavigation(request);
    }

    private bool IsOwnOrConfigured(HttpRequest request, string origin) =>
        string.Equals(origin, $"{request.Scheme}://{request.Host}", StringComparison.OrdinalIgnoreCase)
        || _allowedOrigins.Contains(origin);

    /// <summary>A link followed from another site: a GET or HEAD document navigation, whose response the other site cannot read.</summary>
    private static bool IsTopLevelNavigation(HttpRequest request) =>
        (HttpMethods.IsGet(request.Method) || HttpMethods.IsHead(request.Method))
        && request.Headers["Sec-Fetch-Mode"].ToString() == "navigate"
        && request.Headers["Sec-Fetch-Dest"].ToString() == "document";
}
