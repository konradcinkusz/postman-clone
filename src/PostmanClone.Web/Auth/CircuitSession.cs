namespace PostmanClone.Web.Auth;

/// <summary>
/// The circuit's route to its session's access token, for a component that calls a backend (WP13 onward). A component
/// never sees the token it gets back: it passes it to <c>AuthenticatedRequest</c> and drops it. Null means the session
/// has ended, and the caller sends the user to sign in.
/// </summary>
public sealed class CircuitSession(WorkspaceState workspace, TokenRefresher refresher)
{
    public async Task<string?> AccessTokenAsync(CancellationToken cancellationToken)
    {
        return workspace.SessionId is { } id
            ? await refresher.AccessTokenAsync(id, touch: true, cancellationToken)
            : null;
    }
}
