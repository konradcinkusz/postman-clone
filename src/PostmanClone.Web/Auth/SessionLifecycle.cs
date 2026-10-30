using PostmanClone.Web.Api;

namespace PostmanClone.Web.Auth;

/// <summary>Opens and ends sessions. The only code that moves a token pair between authservice and the store.</summary>
public sealed class SessionLifecycle(ISessionStore sessions, AccessTokenVerifier verifier, AuthServiceClient authservice, TokenRefresher refresher)
{
    /// <summary>Verifies the pair authservice just issued and opens a session for it. Null when the access token does not verify.</summary>
    public async Task<ServerSession?> StartAsync(TokenResponse tokens, CancellationToken cancellationToken)
    {
        var verified = await verifier.VerifyAsync(tokens.AccessToken, cancellationToken);
        return verified is null
            ? null
            : sessions.Create(new SessionTokens(tokens.AccessToken, tokens.RefreshToken, verified.ExpiresAt), verified.Claims);
    }

    /// <summary>
    /// Revokes the user's refresh tokens at authservice, then forgets the session here. Revocation is best effort: the
    /// local session ends either way, so a sign-out never leaves a usable session behind.
    /// </summary>
    public async Task EndAsync(string sessionId, CancellationToken cancellationToken)
    {
        var accessToken = await refresher.AccessTokenAsync(sessionId, touch: false, cancellationToken);
        if (accessToken is not null)
        {
            await authservice.LogoutAsync(accessToken, cancellationToken);
        }

        sessions.Remove(sessionId);
    }
}
