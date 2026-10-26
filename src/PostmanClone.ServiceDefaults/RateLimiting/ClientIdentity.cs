using System.Net;
using Microsoft.AspNetCore.Http;

namespace PostmanClone.ServiceDefaults.RateLimiting;

/// <summary>
/// The one place a client is resolved (SERVICE-API-PATTERNS §1): every limit meters the same visitor, so the limiter
/// and any endpoint that needs the client call the same functions.
/// </summary>
public static class ClientIdentity
{
    /// <summary>The signed-in user (<c>user:{sub}</c>), or the client address when no user is signed in.</summary>
    public static string ForUser(HttpContext context, bool trustForwardedFor)
    {
        var subject = context.User.Identity?.IsAuthenticated == true ? context.User.FindFirst("sub")?.Value : null;
        return subject is { Length: > 0 } ? $"user:{subject}" : ForAddress(context, trustForwardedFor);
    }

    public static string ForAddress(HttpContext context, bool trustForwardedFor) => $"ip:{Address(context, trustForwardedFor)}";

    /// <summary>
    /// The client address. The forwarded header is read only when <c>Proxy:TrustForwardedFor</c> says one proxy sits in
    /// front, and then its <em>last</em> entry is used: the proxy appends the address it saw, while the left-most
    /// entries are whatever the client sent.
    /// </summary>
    public static string Address(HttpContext context, bool trustForwardedFor)
    {
        if (trustForwardedFor && context.Request.Headers.TryGetValue("X-Forwarded-For", out var forwarded))
        {
            var last = forwarded.ToString().Split(',')[^1].Trim();
            if (IPAddress.TryParse(last, out var address))
            {
                return address.ToString();
            }
        }

        return context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    }
}
