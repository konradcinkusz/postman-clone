using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;

namespace PostmanClone.ServiceDefaults.Security;

/// <summary>
/// Refuses requests a page on another site could make through the user's browser (BLAZOR-WEB-APP §8): an
/// <c>Origin</c> that is neither this host nor listed in <c>AllowedOrigins</c>, and a <c>Sec-Fetch-Site: cross-site</c>
/// request other than a top-level GET/HEAD navigation, and on an unsafe method (anything but GET, HEAD or OPTIONS) a
/// <c>Referer</c> that is neither this host nor listed in <c>AllowedOrigins</c>. <c>Origin: null</c> is refused. A request
/// with no <c>Origin</c> (curl, the service's own HttpClient, health probes) is allowed. This is a mitigation, not
/// authentication, and it grants no CORS.
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

        // An unsafe request names its page in Referer too; a page on another site is refused even when Origin is absent.
        // An unreadable Referer on an unsafe request is refused as well: the guard fails closed.
        if (IsUnsafe(request.Method)
            && request.Headers.TryGetValue("Referer", out var referer)
            && !IsOwnOrConfiguredReferer(request, referer.ToString()))
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

    private bool IsOwnOrConfiguredReferer(HttpRequest request, string referer) =>
        Uri.TryCreate(referer, UriKind.Absolute, out var uri)
        && IsOwnOrConfigured(request, uri.GetLeftPart(UriPartial.Authority));

    private static bool IsUnsafe(string method) =>
        !HttpMethods.IsGet(method) && !HttpMethods.IsHead(method) && !HttpMethods.IsOptions(method);

    /// <summary>A link followed from another site: a GET or HEAD document navigation, whose response the other site cannot read.</summary>
    private static bool IsTopLevelNavigation(HttpRequest request) =>
        (HttpMethods.IsGet(request.Method) || HttpMethods.IsHead(request.Method))
        && request.Headers["Sec-Fetch-Mode"].ToString() == "navigate"
        && request.Headers["Sec-Fetch-Dest"].ToString() == "document";
}
