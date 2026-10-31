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

    private readonly Dictionary<Guid, FakeOrganization> _organizations = [];
    private readonly List<FakeInvitation> _invitations = [];
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

    /// <summary>When set, the signed-in address has not confirmed it: accepting an invitation answers 403 (docs/AUTH.md).</summary>
    public bool EmailNotVerified { get; set; }

    public int InviteCalls { get; private set; }

    public int AcceptCalls { get; private set; }

    /// <summary>The organization the last invite was sent for, to check that the Web never takes it from the form.</summary>
    public string? LastInviteOrganization { get; private set; }

    /// <summary>The bearer the last invite carried, to check that the Web sends the session's own token.</summary>
    public string? LastInviteBearer { get; private set; }

    /// <summary>
    /// Makes the signed-in address a member of the organization with the role, the way authservice does on create and accept.
    /// An organization it does not know yet is added under the name given.
    /// </summary>
    public void Join(Guid organization, string name, string role)
    {
        var org = Organization(organization, name);
        org.Members.RemoveAll(member => member.Email == KnownEmail);
        org.Members.Add(new FakeMember(KnownEmail, role));
    }

    /// <summary>Adds another member, for the members list. The signed-in address is not affected.</summary>
    public void AddMember(Guid organization, string email, string role) => Organization(organization, "Acme").Members.Add(new FakeMember(email, role));

    /// <summary>
    /// Creates an invitation the way authservice does on invite (the token is random) and returns the token. A test can
    /// seed an expired or already-accepted one.
    /// </summary>
    public string SeedInvitation(Guid organization, string email, string role, bool expired = false, bool accepted = false)
    {
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
        Organization(organization, "Invited organization");
        _invitations.Add(new FakeInvitation(organization, token, email, role) { Expired = expired, Accepted = accepted });
        return token;
    }

    /// <summary>The role the signed-in address holds in the organization now, or null when it is not a member.</summary>
    public string? RoleIn(Guid organization) => _organizations.TryGetValue(organization, out var org)
        ? org.Members.Find(member => member.Email == KnownEmail)?.Role
        : null;

    public void Reset()
    {
        _organizations.Clear();
        _invitations.Clear();
        _unspentRefreshTokens.Clear();
        LoginCalls = RefreshCalls = LogoutCalls = RegisterCalls = InviteCalls = AcceptCalls = 0;
        RegistrationNeedsConfirmation = EmailNotVerified = false;
        LastLogoutBearer = LastAccessToken = LastRefreshToken = LastInviteOrganization = LastInviteBearer = null;
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
            ("POST", "/api/v1/organizations/invitations/accept") => AcceptInvitation(request),
            _ when path.StartsWith("/api/v1/organizations/", StringComparison.Ordinal) => Organization(request, path),
            _ => Json(HttpStatusCode.NotFound, new { code = "not_found" }),
        };
    }

    /// <summary>The per-organization routes: the detail, the invite, and the invitation list (docs/AUTH.md).</summary>
    private HttpResponseMessage Organization(HttpRequestMessage request, string path)
    {
        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length < 4 || !Guid.TryParse(segments[3], out var id))
        {
            return Json(HttpStatusCode.NotFound, new { code = "not_found" });
        }

        var role = RoleIn(id);
        return (request.Method.Method, segments.Length > 4 ? segments[4] : null) switch
        {
            ("GET", null) => role is null
                ? Json(HttpStatusCode.NotFound, new { error = "Organization not found or access denied" })
                : Json(HttpStatusCode.OK, Detail(id)),
            ("POST", "invite") => Invite(request, id, role),
            ("GET", "invitations") => role is "Owner" or "Admin"
                ? Json(HttpStatusCode.OK, PendingInvitations(id))
                : Json(HttpStatusCode.Forbidden, new { }),
            _ => Json(HttpStatusCode.NotFound, new { code = "not_found" }),
        };
    }

    private HttpResponseMessage Invite(HttpRequestMessage request, Guid organization, string? role)
    {
        InviteCalls++;
        LastInviteOrganization = organization.ToString();
        LastInviteBearer = request.Headers.Authorization?.Parameter;

        // authservice's own checks, in its order: the caller's role, the role's shape, the members, a pending invitation, the level.
        if (role is not ("Owner" or "Admin"))
        {
            return Json(HttpStatusCode.Forbidden, new { });
        }

        var body = Body(request);
        var email = body.GetProperty("email").GetString() ?? string.Empty;
        var inviteRole = body.GetProperty("role").GetString() ?? string.Empty;
        if (inviteRole is not ("Owner" or "Admin" or "Member"))
        {
            return Json(HttpStatusCode.BadRequest, new { errors = new { Role = new[] { "Role must be Owner, Admin, or Member" } } });
        }

        if (_organizations[organization].Members.Exists(member => member.Email == email))
        {
            return Json(HttpStatusCode.BadRequest, new { error = "User is already a member of this organization" });
        }

        if (_invitations.Exists(invitation => invitation.Organization == organization && invitation.Email == email && !invitation.Accepted && !invitation.Expired))
        {
            return Json(HttpStatusCode.BadRequest, new { error = "An invitation has already been sent to this email" });
        }

        if (inviteRole == "Owner" && role != "Owner")
        {
            return Json(HttpStatusCode.Forbidden, new { error = "Only an Owner can invite a new member at the Owner role." });
        }

        var token = SeedInvitation(organization, email, inviteRole);
        return Json(HttpStatusCode.OK, new
        {
            id = Guid.NewGuid().ToString(),
            organizationId = organization.ToString(),
            organizationName = _organizations[organization].Name,
            email,
            role = inviteRole,
            emailStatus = "Pending",
            isAccepted = false,
            token,
        });
    }

    private HttpResponseMessage AcceptInvitation(HttpRequestMessage request)
    {
        AcceptCalls++;
        if (EmailNotVerified)
        {
            return Json(HttpStatusCode.Forbidden, new { error = "Verify your email address before accepting an organization invitation.", emailVerificationRequired = true });
        }

        var token = Body(request).GetProperty("token").GetString() ?? string.Empty;
        var invitation = _invitations.Find(candidate => candidate.Token == token && candidate.Email == KnownEmail);
        if (invitation is null)
        {
            return Json(HttpStatusCode.NotFound, new { error = "Invitation not found" });
        }

        if (invitation.Accepted)
        {
            return Json(HttpStatusCode.BadRequest, new { error = "Invitation has already been accepted" });
        }

        if (invitation.Expired)
        {
            return Json(HttpStatusCode.BadRequest, new { error = "Invitation has expired" });
        }

        // A membership that already exists answers 200 as well: the invitation is spent and nothing new is joined.
        invitation.Accepted = true;
        if (RoleIn(invitation.Organization) is null)
        {
            Join(invitation.Organization, _organizations[invitation.Organization].Name, invitation.Role);
        }

        return Json(HttpStatusCode.OK, new { message = "Invitation accepted successfully", organizationId = invitation.Organization.ToString() });
    }

    private object Detail(Guid organization)
    {
        var org = _organizations[organization];
        return new
        {
            id = organization.ToString(),
            name = org.Name,
            description = (string?)null,
            imageUrl = (string?)null,
            createdAt = DateTime.UtcNow,
            members = org.Members.Select(member => (object)new
            {
                userId = member.Email,
                email = member.Email,
                userName = member.Email,
                profileImageUrl = (string?)null,
                role = member.Role,
                joinedAt = DateTime.UtcNow,
            }).ToArray(),
        };
    }

    private object[] PendingInvitations(Guid organization) =>
        _invitations
            .Where(invitation => invitation.Organization == organization && !invitation.Accepted && !invitation.Expired)
            .Select(invitation => (object)new
            {
                id = invitation.Token,
                organizationId = organization.ToString(),
                organizationName = _organizations[organization].Name,
                email = invitation.Email,
                role = invitation.Role,
                createdAt = DateTime.UtcNow,
                expiresAt = DateTime.UtcNow.AddDays(7),
                isAccepted = false,
                emailStatus = "Pending",
                emailAttemptCount = 0,
            })
            .ToArray();

    private FakeOrganization Organization(Guid id, string name)
    {
        if (!_organizations.TryGetValue(id, out var org))
        {
            org = new FakeOrganization(name);
            _organizations[id] = org;
        }

        return org;
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
        foreach (var (organization, org) in _organizations)
        {
            if (org.Members.Find(member => member.Email == KnownEmail) is { } membership)
            {
                claims.AddRange(TestTokens.Membership(organization, membership.Role));
            }
        }

        var access = _tokens.Issue(claims, TimeSpan.FromHours(1), issuer: Issuer);
        var refresh = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        _unspentRefreshTokens.Add(refresh);
        LastAccessToken = access;
        LastRefreshToken = refresh;
        return Json(status, new { accessToken = access, refreshToken = refresh, expiresIn = 3600, tokenType = "Bearer" });
    }

    private object[] Organizations() =>
        _organizations
            .Where(entry => entry.Value.Members.Exists(member => member.Email == KnownEmail))
            .Select(entry => (object)new
            {
                id = entry.Key.ToString(),
                name = entry.Value.Name,
                imageUrl = (string?)null,
                role = entry.Value.Members.Find(member => member.Email == KnownEmail)!.Role,
            })
            .ToArray();

    private sealed class FakeOrganization(string name)
    {
        public string Name { get; } = name;

        public List<FakeMember> Members { get; } = [];
    }

    private sealed record FakeMember(string Email, string Role);

    private sealed class FakeInvitation(Guid organization, string token, string email, string role)
    {
        public Guid Organization { get; } = organization;

        public string Token { get; } = token;

        public string Email { get; } = email;

        public string Role { get; } = role;

        public bool Accepted { get; set; }

        public bool Expired { get; set; }
    }

    private static System.Text.Json.JsonElement Body(HttpRequestMessage request) =>
        System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(request.Content!.ReadAsStringAsync().GetAwaiter().GetResult());

    private static HttpResponseMessage Json(HttpStatusCode status, object body) =>
        new(status) { Content = JsonContent.Create(body) };
}
