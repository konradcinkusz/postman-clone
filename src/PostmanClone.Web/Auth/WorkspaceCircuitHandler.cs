using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Server.Circuits;

namespace PostmanClone.Web.Auth;

/// <summary>Captures the signed-in user when the circuit opens, so the workspace is known before the first component renders.</summary>
public sealed class WorkspaceCircuitHandler(WorkspaceState workspace, AuthenticationStateProvider provider) : CircuitHandler
{
    public override Task OnCircuitOpenedAsync(Circuit circuit, CancellationToken cancellationToken) =>
        workspace.CaptureAsync(provider);
}
