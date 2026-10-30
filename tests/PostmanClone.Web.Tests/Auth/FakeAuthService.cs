using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using PostmanClone.TestKit;
using PostmanClone.Web.Api;

namespace PostmanClone.Web.Tests.Auth;

/// <summary>
/// Stands in for authservice behind the Web's <c>authservice</c> client (WP12 acceptance: "a fake authservice handler").
/// It answers the endpoints the Web calls with the shapes docs/AUTH.md records, signs its tokens with the test key, and
/// keeps refresh tokens single-use, as authservice does: a spent one is refused. It counts the calls the tests assert on.
/// </summary>
public sealed class FakeAuthService
{
    /// <summary>authservice's issuer in this repository (<c>Jwt:Issuer</c>, docs/AUTH.md). The Web validates against it.</summary>
    public const string Issuer = "PostmanClone";

    public const string KnownEmail = "person@example.test";
    public const string KnownPassword = "correct-password"; // gitleaks:allow — a test fixture, not a credential.

    private readonly Dictionary<Guid, (string Name, string Role)> _organizations = [];
    private readonly HashSet<string> _unspentRefreshTokens = new(StringComparer.Ordinal);

    private readonly TestTokens _tokens;

    public FakeAuthService(TestTokens tokens)
    {
        _tokens = tokens;
        Handler = new FakeHttpMessageHandler(Respond);
    }

    public FakeHttpMessageHandler Handler { get; }

    public int LoginCalls { get; private set; }

    public int RefreshCalls { get; private set; }

    public int LogoutCalls { get; private set; }

    /// <summary>The bearer the last logout carried, to check that sign-out revokes the session's own token.</summary>
    public string? LastLogoutBearer { get; private set; }

    /// <summary>The access token and refresh token the Web last received, to check that neither reaches the page.</summary>
    public string? LastAccessToken { get; private set; }

    public string? LastRefreshToken { get; private set; }

    /// <summary>When set, a login answers with a two-factor challenge.</summary>
    public bool TwoFactorRequired { get; set; }

    /// <summary>When set, a registration waits for the address to be confirmed: <c>202</c>, no tokens (docs/AUTH.md, row 4).</summary>
    public bool RegistrationNeedsConfirmation { get; set; }

    public int RegisterCalls { get; private set; }

    /// <summary>When set, authservice cannot be reached: every call answers 503.</summary>
    public bool Unavailable { get; set; }

    public void Join(Guid organization, string name, string role) => _organizations[organization] = (name, role);

    public void Reset()
    {
        _organizations.Clear();
        _unspentRefreshTokens.Clear();
        LoginCalls = RefreshCalls = LogoutCalls = RegisterCalls = 0;
        RegistrationNeedsConfirmation = false;
        LastLogoutBearer = LastAccessToken = LastRefreshToken = null;
        TwoFactorRequired = Unavailable = false;
    }

    private HttpResponseMessage Respond(HttpRequestMessage request)
    {
        var path = request.RequestUri!.AbsolutePath;
        if (Unavailable)
        {
            return Json(HttpStatusCode.ServiceUnavailable, new { message = "down" });
        }

        return (request.Method.Method, path) switch
        {
            ("POST", "/api/v1/auth/login") => Login(request),
            ("POST", "/api/v1/auth/register") => Register(),
            ("POST", "/api/v1/auth/refresh") => Refresh(request),
            ("POST", "/api/v1/auth/logout") => Logout(request),
            ("GET", "/api/v1/auth/me") => Json(HttpStatusCode.OK, new { id = Guid.NewGuid().ToString(), email = KnownEmail, organizations = Organizations() }),
            ("GET", "/api/v1/auth/consents/versions") => Json(HttpStatusCode.OK, new { terms = "terms-v1", privacy = "privacy-v1", cookies = "cookies-v1" }),
            ("POST", "/api/v1/organizations") => CreateOrganization(request),
            _ => Json(HttpStatusCode.NotFound, new { code = "not_found" }),
        };
    }

    private HttpResponseMessage Login(HttpRequestMessage request)
    {
        LoginCalls++;
        var body = Body(request);
        if (body.GetProperty("email").GetString() != KnownEmail || body.GetProperty("password").GetString() != KnownPassword)
        {
            return Json(HttpStatusCode.Unauthorized, new { message = "invalid" });
        }

        if (TwoFactorRequired)
        {
            return Json(HttpStatusCode.OK, new { requiresTwoFactor = true, challengeToken = "challenge", expiresIn = 300 });
        }

        return IssuePair(HttpStatusCode.OK);
    }

    private HttpResponseMessage Register()
    {
        RegisterCalls++;
        return RegistrationNeedsConfirmation
            ? Json(HttpStatusCode.Accepted, new { message = "confirm your email" })
            : IssuePair(HttpStatusCode.OK);
    }

    private HttpResponseMessage Refresh(HttpRequestMessage request)
    {
        RefreshCalls++;
        var spent = Body(request).GetProperty("refreshToken").GetString() ?? string.Empty;
        if (!_unspentRefreshTokens.Remove(spent))
        {
            return Json(HttpStatusCode.Unauthorized, new { message = "refresh token is not valid" });
        }

        return IssuePair(HttpStatusCode.OK);
    }

    private HttpResponseMessage Logout(HttpRequestMessage request)
    {
        LogoutCalls++;
        LastLogoutBearer = request.Headers.Authorization?.Parameter;
        _unspentRefreshTokens.Clear();
        return Json(HttpStatusCode.OK, new { message = "logged out" });
    }

    private HttpResponseMessage CreateOrganization(HttpRequestMessage request)
    {
        var name = Body(request).GetProperty("name").GetString() ?? string.Empty;
        var id = Guid.NewGuid();
        Join(id, name, "Owner");
        return Json(HttpStatusCode.Created, new { id = id.ToString(), name, userRole = "Owner" });
    }

    /// <summary>A fresh pair: an access token with the current memberships, and a refresh token that is good once.</summary>
    private HttpResponseMessage IssuePair(HttpStatusCode status)
    {
        var claims = new List<Claim> { new("email", KnownEmail) };
        foreach (var (organization, (_, role)) in _organizations)
        {
            claims.AddRange(TestTokens.Membership(organization, role));
        }

        var access = _tokens.Issue(claims, TimeSpan.FromHours(1), issuer: Issuer);
        var refresh = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        _unspentRefreshTokens.Add(refresh);
        LastAccessToken = access;
        LastRefreshToken = refresh;
        return Json(status, new { accessToken = access, refreshToken = refresh, expiresIn = 3600, tokenType = "Bearer" });
    }

    private object[] Organizations() =>
        _organizations.Select(entry => (object)new { id = entry.Key.ToString(), name = entry.Value.Name, imageUrl = (string?)null, role = entry.Value.Role }).ToArray();

    private static System.Text.Json.JsonElement Body(HttpRequestMessage request) =>
        System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(request.Content!.ReadAsStringAsync().GetAwaiter().GetResult());

    private static HttpResponseMessage Json(HttpStatusCode status, object body) =>
        new(status) { Content = JsonContent.Create(body) };
}
