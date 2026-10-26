using System.Security.Claims;
using PostmanClone.ServiceDefaults.Authentication;
using PostmanClone.TestKit;
using Xunit;

namespace PostmanClone.ServiceDefaults.Tests.Authentication;

/// <summary>CONTRACTS §0: the workspace is the single <c>organization</c> claim, with a known role for it. Anything else fails closed.</summary>
public sealed class WorkspaceClaimsTests
{
    private static readonly Guid Acme = new("3f1c9a52-7d4e-4b8a-9e21-0c6d5b7a8f10");
    private static readonly Guid Globex = new("a94e2d17-5b3c-4f60-8d9a-e7c1f2b40a63");

    [Theory]
    [InlineData(0, null, WorkspaceOutcomes.None)]
    [InlineData(1, WorkspaceRoles.Owner, WorkspaceOutcomes.Active)]
    [InlineData(1, WorkspaceRoles.Admin, WorkspaceOutcomes.Active)]
    [InlineData(1, WorkspaceRoles.Member, WorkspaceOutcomes.Active)]
    [InlineData(1, null, WorkspaceOutcomes.None)]
    [InlineData(1, "Viewer", WorkspaceOutcomes.None)]
    [InlineData(2, WorkspaceRoles.Owner, WorkspaceOutcomes.Multiple)]
    [InlineData(2, null, WorkspaceOutcomes.Multiple)]
    public void The_outcome_follows_the_number_of_organizations_and_the_role(int organizations, string? role, string expected)
    {
        var claims = new List<Claim>();
        var memberships = new[] { Acme, Globex }.Take(organizations);
        foreach (var organization in memberships)
        {
            claims.Add(new Claim(WorkspaceClaims.OrganizationClaim, organization.ToString()));
            if (role is not null && organization == Acme)
            {
                claims.Add(new Claim(WorkspaceClaims.RoleClaimFor(organization.ToString()), role));
            }
        }

        var resolution = WorkspaceClaims.Resolve(Principal(claims));

        Assert.Equal(expected, resolution.Outcome);
    }

    [Fact]
    public void An_active_workspace_carries_the_organization_and_the_role()
    {
        var resolution = WorkspaceClaims.Resolve(Principal(TestTokens.Membership(Acme, WorkspaceRoles.Admin)));

        Assert.True(resolution.IsActive);
        Assert.Equal(Acme, resolution.WorkspaceId);
        Assert.Equal(WorkspaceRoles.Admin, resolution.Role);
    }

    [Fact]
    public void The_same_membership_listed_twice_is_one_workspace()
    {
        var claims = TestTokens.Membership(Acme, WorkspaceRoles.Member).Concat(TestTokens.Membership(Acme, WorkspaceRoles.Member));

        var resolution = WorkspaceClaims.Resolve(Principal(claims));

        Assert.Equal(WorkspaceOutcomes.Active, resolution.Outcome);
        Assert.Equal(Acme, resolution.WorkspaceId);
    }

    [Fact]
    public void A_role_issued_for_another_organization_does_not_count()
    {
        var claims = new[]
        {
            new Claim(WorkspaceClaims.OrganizationClaim, Acme.ToString()),
            new Claim(WorkspaceClaims.RoleClaimFor(Globex.ToString()), WorkspaceRoles.Owner),
        };

        var resolution = WorkspaceClaims.Resolve(Principal(claims));

        Assert.Equal(WorkspaceOutcomes.None, resolution.Outcome);
    }

    [Fact]
    public void A_malformed_organization_id_is_not_a_workspace()
    {
        var claims = new[]
        {
            new Claim(WorkspaceClaims.OrganizationClaim, "not-a-guid"),
            new Claim(WorkspaceClaims.RoleClaimFor("not-a-guid"), WorkspaceRoles.Owner),
        };

        var resolution = WorkspaceClaims.Resolve(Principal(claims));

        Assert.Equal(WorkspaceOutcomes.None, resolution.Outcome);
    }

    private static ClaimsPrincipal Principal(IEnumerable<Claim> claims) =>
        new(new ClaimsIdentity(claims, authenticationType: "Test"));
}
