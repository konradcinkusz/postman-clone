using System.Security.Claims;
using PostmanClone.CollectionsService.Domain;
using PostmanClone.ServiceDefaults.Authentication;

namespace PostmanClone.CollectionsService.Services;

/// <summary>
/// Who is calling and in which workspace (CONTRACTS §0): the workspace from the token's <c>organization</c> claim, the
/// role from its <c>organization:{id}:role</c> claim and the user from <c>sub</c>. Nothing here comes from a route or body.
/// </summary>
public sealed record Caller(Guid WorkspaceId, string Role, Guid UserId)
{
    /// <summary>The caller of an endpoint behind <c>RequireActiveWorkspace</c>, which guarantees one active workspace.</summary>
    public static Caller From(ClaimsPrincipal principal)
    {
        var workspace = WorkspaceClaims.Resolve(principal);
        var user = Guid.TryParse(principal.FindFirst("sub")?.Value, out var subject) ? subject : Guid.Empty;
        return new Caller(workspace.WorkspaceId!.Value, workspace.Role!, user);
    }

    /// <summary>Deleting a collection is for its creator, an Admin or an Owner (README roles table).</summary>
    public bool MayDelete(Collection collection) =>
        Role is WorkspaceRoles.Owner or WorkspaceRoles.Admin || UserId == collection.CreatedBy;
}
