namespace PostmanClone.Web.Auth;

/// <summary>
/// Decides whether a circuit's session still stands (ADR-0005). It is asked every <see cref="Interval"/>. A session
/// that is idle past its timeout, past its absolute lifetime, or whose refresh fails, is dead, and its circuit's user
/// is sent to sign in again. The check is not user activity, so an open but idle tab does not keep a session alive.
/// </summary>
public sealed class SessionRevalidator(TokenRefresher refresher)
{
    public static readonly TimeSpan Interval = TimeSpan.FromMinutes(5);

    public async Task<bool> IsStillValidAsync(string sessionId, CancellationToken cancellationToken) =>
        await refresher.AccessTokenAsync(sessionId, touch: false, cancellationToken) is not null;
}
