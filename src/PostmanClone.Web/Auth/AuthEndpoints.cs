using PostmanClone.ServiceDefaults.RateLimiting;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using PostmanClone.Web.Api;

namespace PostmanClone.Web.Auth;

/// <summary>
/// The form posts of the sign-in flow (WP12, FRONTEND-BFF §3). They are minimal endpoints, not component handlers: a
/// response that sets a cookie must be a real HTTP response, and the antiforgery check applies to any endpoint that
/// reads a form. The form pages post here natively, so no JavaScript is needed (ADR-0009). Every answer that sets or
/// clears a cookie is <c>private, no-store</c> (ADR-0005).
/// </summary>
public static class AuthEndpoints
{
    public static WebApplication MapAuthEndpoints(this WebApplication app)
    {
        // The sign-in and register forms are the ones an attacker guesses through (OI-02): a per-address window.
        app.MapPost("/sign-in", SignInAsync).AllowAnonymous().RequireRateLimiting(RateLimitPolicies.Auth);
        app.MapPost("/register", RegisterAsync).AllowAnonymous().RequireRateLimiting(RateLimitPolicies.Auth);
        app.MapPost("/sign-out", SignOutAsync).AllowAnonymous();
        app.MapPost("/no-workspace/create", CreateWorkspaceAsync).RequireAuthorization();
        return app;
    }

    private static async Task<IResult> SignInAsync(HttpContext http, IFormCollection form, IWebHostEnvironment environment, AuthServiceClient authservice, SessionLifecycle lifecycle, ISessionStore sessions, CancellationToken cancellationToken)
    {
        var returnUrl = form["returnUrl"].ToString();
        var email = form["email"].ToString().Trim();
        var password = form["password"].ToString();
        if (email.Length == 0 || password.Length == 0)
        {
            return Redirect(http, ReturnUrlPolicy.SignInPath(returnUrl, "invalid"));
        }

        var answer = await authservice.LoginAsync(email, password, cancellationToken);
        switch (answer.Outcome)
        {
            case AuthOutcome.Succeeded:
                var session = await lifecycle.StartAsync(answer.Value!, cancellationToken);
                return session is null
                    ? Redirect(http, ReturnUrlPolicy.SignInPath(returnUrl, "unavailable"))
                    : SignedIn(http, environment, sessions, session, ReturnUrlPolicy.Sanitize(returnUrl));
            case AuthOutcome.TwoFactorRequired:
                return Redirect(http, ReturnUrlPolicy.SignInPath(returnUrl, "two-factor"));
            case AuthOutcome.Rejected:
                return Redirect(http, ReturnUrlPolicy.SignInPath(returnUrl, "invalid"));
            default:
                return Redirect(http, ReturnUrlPolicy.SignInPath(returnUrl, "unavailable"));
        }
    }

    private static async Task<IResult> RegisterAsync(HttpContext http, IFormCollection form, IWebHostEnvironment environment, AuthServiceClient authservice, SessionLifecycle lifecycle, ISessionStore sessions, CancellationToken cancellationToken)
    {
        var email = form["email"].ToString().Trim();
        var password = form["password"].ToString();
        var accepted = form["acceptTerms"].ToString() == "on" && form["acceptPrivacy"].ToString() == "on";
        if (email.Length == 0 || password.Length == 0 || !accepted)
        {
            return Redirect(http, "/register?error=details");
        }

        // The versions are read here, not trusted from the form: the registration quotes what authservice says is current.
        var versions = await authservice.ConsentVersionsAsync(cancellationToken);
        if (versions is null)
        {
            return Redirect(http, "/register?error=unavailable");
        }

        var answer = await authservice.RegisterAsync(email, password, versions.Terms, versions.Privacy, cancellationToken);
        switch (answer.Outcome)
        {
            case AuthOutcome.Succeeded:
                var session = await lifecycle.StartAsync(answer.Value!, cancellationToken);
                return session is null
                    ? Redirect(http, "/register?error=unavailable")
                    : SignedIn(http, environment, sessions, session, ReturnUrlPolicy.Fallback);
            case AuthOutcome.PendingVerification:
                return Redirect(http, "/sign-in?notice=verify");
            case AuthOutcome.Rejected:
                return Redirect(http, "/register?error=rejected");
            default:
                return Redirect(http, "/register?error=unavailable");
        }
    }

    private static async Task<IResult> SignOutAsync(HttpContext http, IFormCollection form, IWebHostEnvironment environment, SessionLifecycle lifecycle, CancellationToken cancellationToken)
    {
        // The form is a parameter so the antiforgery check applies: a sign-out must not be forced by another site.
        var name = CookieName(http, environment);
        if (http.Request.Cookies.TryGetValue(name, out var id) && !string.IsNullOrEmpty(id))
        {
            await lifecycle.EndAsync(id, cancellationToken);
        }

        // Deleted with the attributes it was set with, or the browser keeps it.
        http.Response.Cookies.Delete(name, SessionCookie.Options(SessionCookie.RequiresSecure(environment.IsDevelopment(), http.Request.IsHttps), expires: null));
        NoStore(http);
        return Results.Redirect("/sign-in");
    }

    /// <summary>
    /// Creates the first workspace (CONTRACTS §0 onboarding, ADR-0004): authservice makes the caller Owner, and the
    /// session refreshes so the new <c>organization</c> claim is in its token. The circuit that asked is then replaced
    /// by a fresh page load, which reads the new claim.
    /// </summary>
    private static async Task<IResult> CreateWorkspaceAsync(HttpContext http, IFormCollection form, AuthServiceClient authservice, TokenRefresher refresher, CancellationToken cancellationToken)
    {
        var sessionId = http.User.FindFirst(ServerSession.SessionIdClaim)?.Value;
        if (sessionId is null)
        {
            return Redirect(http, "/sign-in");
        }

        var name = form["name"].ToString().Trim();
        if (name.Length is 0 or > 100)
        {
            return Redirect(http, "/no-workspace?error=name");
        }

        var accessToken = await refresher.AccessTokenAsync(sessionId, touch: true, cancellationToken);
        if (accessToken is null)
        {
            return Redirect(http, ReturnUrlPolicy.SignInPath(ReturnUrlPolicy.Fallback, "ended"));
        }

        if (await authservice.CreateOrganizationAsync(accessToken, name, cancellationToken) != AuthOutcome.Succeeded)
        {
            return Redirect(http, "/no-workspace?error=create");
        }

        if (!await refresher.RefreshNowAsync(sessionId, cancellationToken))
        {
            return Redirect(http, ReturnUrlPolicy.SignInPath(ReturnUrlPolicy.Fallback, "ended"));
        }

        return Redirect(http, ReturnUrlPolicy.Fallback);
    }

    private static IResult SignedIn(HttpContext http, IWebHostEnvironment environment, ISessionStore sessions, ServerSession session, string returnUrl)
    {
        // A session cookie already present belongs to a session this response replaces: it ends, so a fixed id is never reused.
        var name = CookieName(http, environment);
        if (http.Request.Cookies.TryGetValue(name, out var previous) && !string.IsNullOrEmpty(previous))
        {
            sessions.Remove(previous);
        }

        var secure = SessionCookie.RequiresSecure(environment.IsDevelopment(), http.Request.IsHttps);
        http.Response.Cookies.Append(SessionCookie.NameFor(secure), session.Id, SessionCookie.Options(secure, session.AbsoluteExpiresAt));
        NoStore(http);
        return Results.Redirect(returnUrl);
    }

    private static string CookieName(HttpContext http, IWebHostEnvironment environment) =>
        SessionCookie.NameFor(SessionCookie.RequiresSecure(environment.IsDevelopment(), http.Request.IsHttps));

    private static IResult Redirect(HttpContext http, string location)
    {
        NoStore(http);
        return Results.Redirect(location);
    }

    private static void NoStore(HttpContext http) => http.Response.Headers.CacheControl = "private, no-store";
}
