using System.Security.Claims;

namespace PostmanClone.Web.Auth;

/// <summary>The access and refresh tokens of one session. They are never rendered, logged or stored in a principal.</summary>
public sealed record SessionTokens(string AccessToken, string RefreshToken, DateTimeOffset AccessExpiresAt)
{
    /// <summary>The string form names the record and nothing else, so a logged or debugged value leaks no token.</summary>
    public override string ToString() => "SessionTokens(redacted)";
}

/// <summary>
/// One signed-in session, held in the Web's memory (ADR-0008). Its claims are the verified access token's claims; its
/// tokens are reached only through <see cref="Gate"/>, the per-session lock that serializes refresh (ADR-0005).
/// </summary>
public sealed class ServerSession
{
    public const string AuthenticationType = "PostmanCloneSession";

    /// <summary>The claim that names the session on the principal, so a circuit can find its tokens. It is an id, not a token.</summary>
    public const string SessionIdClaim = "postmanclone:session";

    private IReadOnlyList<Claim> _claims;

    internal ServerSession(string id, SessionTokens tokens, IReadOnlyList<Claim> claims, DateTimeOffset absoluteExpiresAt, DateTimeOffset lastAccessedAt)
    {
        Id = id;
        Tokens = tokens;
        _claims = claims;
        AbsoluteExpiresAt = absoluteExpiresAt;
        LastAccessedAt = lastAccessedAt;
    }

    public string Id { get; }

    /// <summary>Held while a refresh is in flight. Concurrent callers wait on it and then read the result.</summary>
    public SemaphoreSlim Gate { get; } = new(1, 1);

    /// <summary>Written only while <see cref="Gate"/> is held, or at creation.</summary>
    public SessionTokens Tokens { get; private set; }

    public DateTimeOffset AbsoluteExpiresAt { get; }

    public DateTimeOffset LastAccessedAt { get; internal set; }

    /// <summary>The principal a request or circuit acts as: the verified claims plus the session id.</summary>
    public ClaimsPrincipal Principal =>
        new(new ClaimsIdentity(_claims.Append(new Claim(SessionIdClaim, Id)), AuthenticationType));

    /// <summary>Replaces the tokens and the claims after a refresh, so the new organizations apply at once.</summary>
    internal void Replace(SessionTokens tokens, IReadOnlyList<Claim> claims)
    {
        Tokens = tokens;
        _claims = claims;
    }
}
