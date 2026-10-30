using PostmanClone.Web.Api;

namespace PostmanClone.Web.Auth;

/// <summary>
/// Hands out a session's access token, refreshing it first when fewer than two minutes remain (WP12, ADR-0005).
/// Refresh is serialized per session: concurrent callers wait on the session's gate, and the first one to refresh
/// serves the rest. Refresh tokens are single-use, so two requests must never spend the same one. A refresh that
/// fails ends the session.
/// </summary>
public sealed class TokenRefresher(ISessionStore sessions, AuthServiceClient authservice, AccessTokenVerifier verifier, TimeProvider clock)
{
    /// <summary>A token with less than this left is refreshed before it is used.</summary>
    public static readonly TimeSpan RefreshWithin = TimeSpan.FromMinutes(2);

    /// <summary>The access token for a request or a circuit, or null when the session has ended: the caller sends the user to sign in.</summary>
    public Task<string?> AccessTokenAsync(string sessionId, bool touch, CancellationToken cancellationToken) =>
        EnsureAsync(sessionId, touch, force: false, cancellationToken);

    /// <summary>Refreshes now, whatever the token's age. Used after the organization was created, so the new claim is read.</summary>
    public async Task<bool> RefreshNowAsync(string sessionId, CancellationToken cancellationToken) =>
        await EnsureAsync(sessionId, touch: true, force: true, cancellationToken) is not null;

    private async Task<string?> EnsureAsync(string sessionId, bool touch, bool force, CancellationToken cancellationToken)
    {
        var session = sessions.Find(sessionId, touch);
        if (session is null)
        {
            return null;
        }

        if (!force && !NeedsRefresh(session))
        {
            return session.Tokens.AccessToken;
        }

        await session.Gate.WaitAsync(cancellationToken);
        try
        {
            // The session may have ended while this caller waited, or a concurrent caller may have refreshed it.
            // That caller's result is this caller's answer.
            if (sessions.Find(sessionId, touch: false) is null)
            {
                return null;
            }

            if (!force && !NeedsRefresh(session))
            {
                return session.Tokens.AccessToken;
            }

            return await RefreshAsync(sessionId, session);
        }
        finally
        {
            session.Gate.Release();
        }
    }

    private async Task<string?> RefreshAsync(string sessionId, ServerSession session)
    {
        // Not cancelled midway: once authservice has spent the refresh token, the new pair must reach the session.
        var answer = await authservice.RefreshAsync(session.Tokens.RefreshToken, CancellationToken.None);
        if (answer.Outcome != AuthOutcome.Succeeded
            || answer.Value is not { } refreshed
            || await verifier.VerifyAsync(refreshed.AccessToken, CancellationToken.None) is not { } verified)
        {
            sessions.Remove(sessionId);
            return null;
        }

        session.Replace(new SessionTokens(refreshed.AccessToken, refreshed.RefreshToken, verified.ExpiresAt), verified.Claims);
        return refreshed.AccessToken;
    }

    private bool NeedsRefresh(ServerSession session) => session.Tokens.AccessExpiresAt - clock.GetUtcNow() < RefreshWithin;
}
