using PostmanClone.Web.Api;
using PostmanClone.Web.Auth;

namespace PostmanClone.Web.Membership;

/// <summary>What the members page shows: the workspace's name and members, and for an Owner or an Admin the pending invitations.</summary>
public sealed record WorkspaceMembers(
    string Name,
    string Role,
    IReadOnlyList<OrganizationMember> Members,
    bool CanInvite,
    IReadOnlyList<InvitationSummary> Invitations);

/// <summary>
/// Reads the members page from authservice (WP24). It runs in the circuit's scope and holds no token: each call asks
/// <see cref="CircuitSession"/> for the session's access token and passes it on. The workspace is the token's
/// <c>organization</c> claim, and nothing the page sends chooses it.
/// </summary>
public sealed class MembershipService(WorkspaceState workspace, CircuitSession session, AuthServiceClient authservice)
{
    /// <summary>The page's data, or null when there is no active workspace, the session has ended or authservice did not answer.</summary>
    public async Task<WorkspaceMembers?> LoadAsync(CancellationToken cancellationToken)
    {
        if (workspace.Resolution is not { IsActive: true, WorkspaceId: { } workspaceId, Role: { } role })
        {
            return null;
        }

        var accessToken = await session.AccessTokenAsync(cancellationToken);
        if (accessToken is null)
        {
            return null;
        }

        var organization = await authservice.GetOrganizationAsync(accessToken, workspaceId.ToString(), cancellationToken);
        if (organization.Value is not { } detail)
        {
            return null;
        }

        // Only an Owner or an Admin may list invitations, so a Member's page never asks authservice for them.
        if (!InvitationRules.CanInvite(role))
        {
            return new WorkspaceMembers(detail.Name, role, detail.Members, CanInvite: false, Invitations: []);
        }

        var invitations = await authservice.ListInvitationsAsync(accessToken, workspaceId.ToString(), cancellationToken);
        return invitations.Value is { } pending
            ? new WorkspaceMembers(detail.Name, role, detail.Members, CanInvite: true, Invitations: pending)
            : null;
    }
}
