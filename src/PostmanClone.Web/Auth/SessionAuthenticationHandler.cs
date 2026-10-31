using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace PostmanClone.Web.Auth;

public static class SessionAuthenticationDefaults
{
    public const string Scheme = "PostmanCloneSession";
}

/// <summary>
/// Reads the session cookie and returns the session's principal (ADR-0005). The cookie is an id; the principal is the
/// server's copy of the verified claims. An unknown, expired or ended session is anonymous, and the cookie is not
/// trusted for anything else. Only the session's claims reach the principal, never a token.
/// </summary>
public sealed class SessionAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory loggers,
    UrlEncoder encoder,
    ISessionStore sessions,
    IWebHostEnvironment environment)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, loggers, encoder)
{
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var name = SessionCookie.NameFor(SessionCookie.RequiresSecure(environment.IsDevelopment(), Request.IsHttps));
        if (!Request.Cookies.TryGetValue(name, out var id) || string.IsNullOrEmpty(id))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var session = sessions.Find(id, touch: true);
        if (session is null)
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var ticket = new AuthenticationTicket(session.Principal, SessionAuthenticationDefaults.Scheme);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }

    /// <summary>
    /// An anonymous request to a protected endpoint is redirected to sign in, with the requested path as the return path.
    /// Without this, the framework answers 401 and the user sees a page with no way forward.
    /// </summary>
    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.Headers.CacheControl = "private, no-store";
        Response.Redirect(ReturnUrlPolicy.SignInPath($"{Request.PathBase}{Request.Path}{Request.QueryString}"));
        return Task.CompletedTask;
    }
}
