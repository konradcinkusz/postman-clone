using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;
using PostmanClone.ServiceDefaults.Authentication;

namespace PostmanClone.Web.Auth;

/// <summary>
/// The circuit's view of the signed-in user and workspace (WP12). It is read once, when the circuit opens, and read
/// again when the authentication state changes, for instance when the revalidator ends the session. It holds no token
/// and no <c>HttpContext</c>: components read it, and it is never persisted.
/// </summary>
public sealed class WorkspaceState : IDisposable
{
    private Task? _captured;
    private AuthenticationStateProvider? _provider;

    public bool IsAuthenticated { get; private set; }

    public WorkspaceResolution Resolution { get; private set; } = WorkspaceResolution.None;

    /// <summary>The session the circuit belongs to, or null when there is none. Used to reach its tokens, never shown.</summary>
    public string? SessionId { get; private set; }

    /// <summary>The short text the topbar shows.</summary>
    public string Label => !IsAuthenticated
        ? "Workspace: not signed in"
        : Resolution.Outcome switch
        {
            WorkspaceOutcomes.Active => $"Workspace: signed in as {Resolution.Role}",
            WorkspaceOutcomes.Multiple => "Workspace: several",
            _ => "Workspace: none yet",
        };

    /// <summary>Reads the circuit's principal once. Later calls return the same task.</summary>
    public Task CaptureAsync(AuthenticationStateProvider provider)
    {
        if (_captured is not null)
        {
            return _captured;
        }

        _provider = provider;
        provider.AuthenticationStateChanged += OnAuthenticationStateChanged;
        _captured = ApplyFromAsync(provider.GetAuthenticationStateAsync());
        return _captured;
    }

    /// <summary>Takes a principal as the circuit's user. Public so the routing rules can be tested without a host.</summary>
    public void Apply(ClaimsPrincipal user)
    {
        IsAuthenticated = user.Identity?.IsAuthenticated == true;
        Resolution = IsAuthenticated ? WorkspaceClaims.Resolve(user) : WorkspaceResolution.None;
        SessionId = IsAuthenticated ? user.FindFirst(ServerSession.SessionIdClaim)?.Value : null;
    }

    public void Dispose()
    {
        if (_provider is not null)
        {
            _provider.AuthenticationStateChanged -= OnAuthenticationStateChanged;
        }
    }

    private async Task ApplyFromAsync(Task<AuthenticationState> state) => Apply((await state).User);

    private void OnAuthenticationStateChanged(Task<AuthenticationState> state) => _captured = ApplyFromAsync(state);
}
