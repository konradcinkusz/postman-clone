using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using PostmanClone.ServiceDefaults.Authentication;
using PostmanClone.Web.Components.Pages;
using PostmanClone.Web.Tests.Auth;

namespace PostmanClone.Web.Tests.Membership;

/// <summary>The members page: what each role sees, and the alert for a failed read (WP24 acceptance, bUnit).</summary>
public sealed class WorkspacePageTests
{
    [Fact]
    public async Task A_member_sees_the_members_and_no_invite_form()
    {
        using var harness = new AuthHarness();
        var organization = Guid.NewGuid();
        var principal = await MembershipPageTestHarness.SignedInAsync(harness, organization, WorkspaceRoles.Member, Xunit.TestContext.Current.CancellationToken);
        using var context = MembershipPageTestHarness.Create(harness, principal);

        var cut = context.Render<Workspace>();

        cut.WaitForState(() => cut.FindAll("[data-testid='members-table']").Count == 1);
        Assert.Equal("Acme", cut.Find("[data-testid='workspace-name']").TextContent);
        Assert.Equal("Member", cut.Find("[data-testid='workspace-role']").TextContent);
        Assert.Contains(FakeAuthService.KnownEmail, cut.Find("[data-testid='member-row']").TextContent);
        Assert.Empty(cut.FindAll("[data-testid='invite-form']"));
        Assert.Empty(cut.FindAll("[data-testid='invitations-section']"));
        Assert.Single(cut.FindAll("[data-testid='members-read-only']"));
    }

    [Fact]
    public async Task An_admin_sees_the_invite_form_with_admin_and_member_as_the_roles()
    {
        using var harness = new AuthHarness();
        var principal = await MembershipPageTestHarness.SignedInAsync(harness, Guid.NewGuid(), WorkspaceRoles.Admin, Xunit.TestContext.Current.CancellationToken);
        using var context = MembershipPageTestHarness.Create(harness, principal);

        var cut = context.Render<Workspace>();

        cut.WaitForState(() => cut.FindAll("[data-testid='invite-form']").Count == 1);
        var roles = cut.FindAll("[data-testid='invite-role'] option").Select(option => option.TextContent).ToArray();
        Assert.Equal(["Member", "Admin"], roles);
        Assert.Equal("Member", cut.Find("[data-testid='invite-role'] option[selected]").TextContent);
        Assert.Empty(cut.FindAll("[data-testid='members-read-only']"));
    }

    [Fact]
    public async Task An_admin_sees_the_pending_invitations_and_no_token_in_the_page()
    {
        using var harness = new AuthHarness();
        var organization = Guid.NewGuid();
        var principal = await MembershipPageTestHarness.SignedInAsync(harness, organization, WorkspaceRoles.Owner, Xunit.TestContext.Current.CancellationToken);
        var token = harness.Auth.SeedInvitation(organization, "invitee@example.test", "Member");
        using var context = MembershipPageTestHarness.Create(harness, principal);

        var cut = context.Render<Workspace>();

        cut.WaitForState(() => cut.FindAll("[data-testid='invitation-row']").Count == 1);
        Assert.Contains("invitee@example.test", cut.Find("[data-testid='invitation-row']").TextContent);
        Assert.DoesNotContain(token, cut.Markup);
    }

    [Fact]
    public async Task A_failed_read_is_an_alert_not_an_exception()
    {
        using var harness = new AuthHarness();
        var principal = await MembershipPageTestHarness.SignedInAsync(harness, Guid.NewGuid(), WorkspaceRoles.Member, Xunit.TestContext.Current.CancellationToken);
        harness.Auth.Unavailable = true;
        using var context = MembershipPageTestHarness.Create(harness, principal);

        var cut = context.Render<Workspace>();

        cut.WaitForState(() => cut.FindAll("[role='alert']").Count == 1);
        Assert.Contains("could not be read", cut.Find("[role='alert']").TextContent);
        Assert.Empty(cut.FindAll("[data-testid='members-table']"));
    }

    [Fact]
    public async Task The_redirect_code_from_an_invite_is_shown_as_a_success_alert()
    {
        using var harness = new AuthHarness();
        var principal = await MembershipPageTestHarness.SignedInAsync(harness, Guid.NewGuid(), WorkspaceRoles.Admin, Xunit.TestContext.Current.CancellationToken);
        using var context = MembershipPageTestHarness.Create(harness, principal);
        context.Services.GetRequiredService<NavigationManager>().NavigateTo("/workspace?notice=invited");

        var cut = context.Render<Workspace>();

        cut.WaitForState(() => cut.FindAll("[data-testid='members-table']").Count == 1);
        var alert = cut.Find("[data-testid='workspace-message']");
        Assert.Equal("alert", alert.GetAttribute("role"));
        Assert.Contains("callout-success", alert.ClassName);
        Assert.Contains("Invitation created", alert.TextContent);
    }

    [Fact]
    public async Task A_refused_invite_code_is_shown_as_an_error_alert()
    {
        using var harness = new AuthHarness();
        var principal = await MembershipPageTestHarness.SignedInAsync(harness, Guid.NewGuid(), WorkspaceRoles.Admin, Xunit.TestContext.Current.CancellationToken);
        using var context = MembershipPageTestHarness.Create(harness, principal);
        context.Services.GetRequiredService<NavigationManager>().NavigateTo("/workspace?error=pending");

        var cut = context.Render<Workspace>();

        cut.WaitForState(() => cut.FindAll("[data-testid='members-table']").Count == 1);
        var alert = cut.Find("[data-testid='workspace-message']");
        Assert.Contains("callout-danger", alert.ClassName);
        Assert.Contains("already waiting", alert.TextContent);
    }
}
