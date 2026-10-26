using System.Security.Claims;

namespace PostmanClone.ServiceDefaults.Authentication;

/// <summary>
/// The workspace a request acts in (CONTRACTS §0, ADR-0004). The workspace is the single <c>organization</c> claim of
/// the token, never a route, query or body value. Anything other than exactly one organization, with a known role
/// for it, is not a workspace: the request fails closed.
/// </summary>
public static class WorkspaceClaims
{
    public const string OrganizationClaim = "organization";

    /// <summary>The role authservice issues for one membership: <c>organization:{id}:role</c>.</summary>
    public static string RoleClaimFor(string organization) => $"organization:{organization}:role";

    public static WorkspaceResolution Resolve(ClaimsPrincipal principal)
    {
        // A membership listed twice is still one workspace; two different organizations are not.
        var organizations = principal.FindAll(OrganizationClaim)
            .Select(claim => claim.Value)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (organizations.Count == 0)
        {
            return WorkspaceResolution.None;
        }

        if (organizations.Count > 1)
        {
            return WorkspaceResolution.Multiple;
        }

        var organization = organizations[0];
        if (!Guid.TryParse(organization, out var workspaceId))
        {
            return WorkspaceResolution.None;
        }

        // A missing or unknown role is not an implicit Member: no role, no access.
        var role = principal.FindFirst(RoleClaimFor(organization))?.Value;
        return role is not null && WorkspaceRoles.All.Contains(role)
            ? WorkspaceResolution.Active(workspaceId, role)
            : WorkspaceResolution.None;
    }
}

/// <summary>The role values authservice issues (CONTRACTS §0). Strings, not an enum: the kernel declares no enums.</summary>
public static class WorkspaceRoles
{
    public const string Owner = "Owner";
    public const string Admin = "Admin";
    public const string Member = "Member";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal) { Owner, Admin, Member };
}

/// <summary>The outcome names of <see cref="WorkspaceResolution"/>. Strings, not an enum: the kernel declares no enums.</summary>
public static class WorkspaceOutcomes
{
    public const string Active = "active";
    public const string None = "none";
    public const string Multiple = "multiple";
}

/// <summary>What <see cref="WorkspaceClaims.Resolve"/> found: an active workspace with the caller's role, none, or several.</summary>
public sealed record WorkspaceResolution
{
    public static readonly WorkspaceResolution None = new() { Outcome = WorkspaceOutcomes.None };
    public static readonly WorkspaceResolution Multiple = new() { Outcome = WorkspaceOutcomes.Multiple };

    public string Outcome { get; private init; } = WorkspaceOutcomes.None;

    public Guid? WorkspaceId { get; private init; }

    public string? Role { get; private init; }

    public bool IsActive => Outcome == WorkspaceOutcomes.Active;

    public static WorkspaceResolution Active(Guid workspaceId, string role) =>
        new() { Outcome = WorkspaceOutcomes.Active, WorkspaceId = workspaceId, Role = role };
}
