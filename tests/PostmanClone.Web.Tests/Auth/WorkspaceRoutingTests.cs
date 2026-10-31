using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;
using PostmanClone.ServiceDefaults.Authentication;
using PostmanClone.TestKit;
using PostmanClone.Web.Auth;

namespace PostmanClone.Web.Tests.Auth;

/// <summary>Zero, one and two organizations route correctly (WP12 acceptance, ADR-0004).</summary>
public sealed class WorkspaceRoutingTests
{
    private static ClaimsPrincipal Signed(params (Guid Organization, string Role)[] memberships)
    {
        var claims = new List<Claim> { new(ServerSession.SessionIdClaim, "session-id"), new("sub", "user") };
        foreach (var (organization, role) in memberships)
        {
            claims.AddRange(TestTokens.Membership(organization, role));
        }

        return new ClaimsPrincipal(new ClaimsIdentity(claims, ServerSession.AuthenticationType));
    }

    [Fact]
    public void A_user_with_one_workspace_reaches_the_domain_pages()
    {
        var principal = Signed((Guid.NewGuid(), WorkspaceRoles.Member));

        Assert.Equal(WorkspaceDecision.Continue, WorkspaceRouting.Decide("/environments", authenticated: true, WorkspaceClaims.Resolve(principal)));
    }

    [Fact]
    public void A_user_with_no_workspace_is_sent_to_the_explanation()
    {
        var principal = Signed();

        Assert.Equal(WorkspaceDecision.RedirectToNoWorkspace, WorkspaceRouting.Decide("/environments", authenticated: true, WorkspaceClaims.Resolve(principal)));
    }

    [Fact]
    public void A_user_with_two_workspaces_is_sent_to_the_explanation_not_given_a_switcher()
    {
        var principal = Signed((Guid.NewGuid(), WorkspaceRoles.Owner), (Guid.NewGuid(), WorkspaceRoles.Member));

        Assert.Equal(WorkspaceDecision.RedirectToNoWorkspace, WorkspaceRouting.Decide("/", authenticated: true, WorkspaceClaims.Resolve(principal)));
    }

    [Theory]
    [InlineData("/no-workspace")]
    [InlineData("/sign-in")]
    [InlineData("/register")]
    [InlineData("/legal/terms")]
    [InlineData("/accept-invitation")]
    public void The_pages_that_work_without_a_workspace_are_exempt(string path)
    {
        Assert.Equal(WorkspaceDecision.Continue, WorkspaceRouting.Decide(path, authenticated: true, WorkspaceResolution.None));
    }

    [Fact]
    public void An_anonymous_visitor_is_left_to_the_authorization_attribute()
    {
        Assert.Equal(WorkspaceDecision.Continue, WorkspaceRouting.Decide("/environments", authenticated: false, WorkspaceResolution.None));
    }

    [Fact]
    public void The_workspace_state_reads_the_principal_it_is_given()
    {
        var state = new WorkspaceState();

        state.Apply(Signed((Guid.NewGuid(), WorkspaceRoles.Admin)));

        Assert.True(state.IsAuthenticated);
        Assert.True(state.Resolution.IsActive);
        Assert.Equal("Workspace: signed in as Admin", state.Label);
        Assert.NotNull(state.SessionId);
    }

    [Fact]
    public void The_workspace_state_names_several_and_none()
    {
        var state = new WorkspaceState();

        state.Apply(Signed((Guid.NewGuid(), WorkspaceRoles.Owner), (Guid.NewGuid(), WorkspaceRoles.Owner)));
        Assert.Equal("Workspace: several", state.Label);

        state.Apply(Signed());
        Assert.Equal("Workspace: none yet", state.Label);
    }

    [Fact]
    public void An_anonymous_principal_has_no_workspace_and_no_session()
    {
        var state = new WorkspaceState();

        state.Apply(new ClaimsPrincipal(new ClaimsIdentity()));

        Assert.False(state.IsAuthenticated);
        Assert.Null(state.SessionId);
        Assert.Equal("Workspace: not signed in", state.Label);
    }
}
