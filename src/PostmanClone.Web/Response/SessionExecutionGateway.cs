using PostmanClone.Web.Api;
using PostmanClone.Web.Auth;

namespace PostmanClone.Web.Response;

/// <summary>
/// The production gateway: the circuit's access token and its one workspace, then the execution client (ADR-0022). No workspace
/// and no session are outcomes, so the page can say which, as the shell does.
/// </summary>
public sealed class SessionExecutionGateway(WorkspaceState workspace, CircuitSession session, IExecutionClient client) : IExecutionGateway
{
    public async Task<CollectionAnswer<ExecutionResultDto>> ExecuteAsync(ExecuteBody body, CancellationToken cancellationToken)
    {
        if (!workspace.Resolution.IsActive)
        {
            return new CollectionAnswer<ExecutionResultDto>(CollectionOutcome.NoWorkspace, null, null);
        }

        var token = await session.AccessTokenAsync(cancellationToken);
        if (token is null)
        {
            return new CollectionAnswer<ExecutionResultDto>(CollectionOutcome.SessionEnded, null, null);
        }

        return await client.ExecuteAsync(token, body, cancellationToken);
    }
}
