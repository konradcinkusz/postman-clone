using Microsoft.AspNetCore.Http;

namespace PostmanClone.Web.Auth;

/// <summary>
/// The session cookie (ADR-0005). It carries only the random session id; the ticket and the token pair stay in
/// <see cref="ServerSessionStore"/>. Outside Development it is <c>Secure</c> with the <c>__Host-</c> prefix, which
/// stops a sibling subdomain from setting it. Development over plain http cannot carry a Secure cookie, so it gets the
/// unprefixed name, and only there.
/// </summary>
public static class SessionCookie
{
    public const string SecureName = "__Host-pc.session";
    public const string DevelopmentName = "pc.session";

    public static bool RequiresSecure(bool isDevelopment, bool isHttps) => !isDevelopment || isHttps;

    public static string NameFor(bool secure) => secure ? SecureName : DevelopmentName;

    /// <summary>
    /// The attributes the cookie is set with. Logout deletes it with the same attributes: a delete with a different path
    /// or SameSite leaves the cookie alive (FRONTEND-BFF §3).
    /// </summary>
    public static CookieOptions Options(bool secure, DateTimeOffset? expires) => new()
    {
        HttpOnly = true,
        Secure = secure,
        SameSite = SameSiteMode.Strict,
        Path = "/",
        Expires = expires,
        IsEssential = true,
    };
}
