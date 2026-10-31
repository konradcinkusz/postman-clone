using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using PostmanClone.ServiceDefaults.Authentication;
using PostmanClone.Web.Components.Pages;
using PostmanClone.Web.Tests.Auth;

namespace PostmanClone.Web.Tests.Membership;

/// <summary>The accept page: the confirm form, the missing-link text, the codes, and the warning for a second workspace.</summary>
public sealed class AcceptInvitationPageTests
{
    [Fact]
    public async Task With_a_token_the_page_offers_the_accept_form_and_sends_the_token_back_as_a_hidden_field()
    {
        using var harness = new AuthHarness();
        var principal = await MembershipPageTestHarness.SignedInAsync(harness, Guid.NewGuid(), WorkspaceRoles.Member, Xunit.TestContext.Current.CancellationToken);
        using var context = MembershipPageTestHarness.Create(harness, principal);
        context.Services.GetRequiredService<NavigationManager>().NavigateTo("/accept-invitation?token=abc-DEF_123");

        var cut = context.Render<AcceptInvitation>();

        var hidden = cut.Find("[data-testid='accept-form'] input[name='token']");
        Assert.Equal("abc-DEF_123", hidden.GetAttribute("value"));
        Assert.Empty(cut.FindAll("[data-testid='accept-no-token']"));
    }

    [Fact]
    public async Task Without_a_token_the_page_says_the_link_is_incomplete_and_offers_no_form()
    {
        using var harness = new AuthHarness();
        var principal = await MembershipPageTestHarness.SignedInAsync(harness, Guid.NewGuid(), WorkspaceRoles.Member, Xunit.TestContext.Current.CancellationToken);
        using var context = MembershipPageTestHarness.Create(harness, principal);
        context.Services.GetRequiredService<NavigationManager>().NavigateTo("/accept-invitation");

        var cut = context.Render<AcceptInvitation>();

        Assert.Single(cut.FindAll("[data-testid='accept-no-token']"));
        Assert.Empty(cut.FindAll("[data-testid='accept-form']"));
    }

    [Fact]
    public async Task An_expired_code_is_an_alert_and_needs_no_token_to_show()
    {
        using var harness = new AuthHarness();
        var principal = await MembershipPageTestHarness.SignedInAsync(harness, Guid.NewGuid(), WorkspaceRoles.Member, Xunit.TestContext.Current.CancellationToken);
        using var context = MembershipPageTestHarness.Create(harness, principal);
        context.Services.GetRequiredService<NavigationManager>().NavigateTo("/accept-invitation?error=expired");

        var cut = context.Render<AcceptInvitation>();

        var alert = cut.Find("[role='alert']");
        Assert.Contains("has expired", alert.TextContent);
        Assert.Empty(cut.FindAll("[data-testid='accept-form']"));
        Assert.Empty(cut.FindAll("[data-testid='accept-no-token']"));
    }

    [Fact]
    public async Task A_user_who_already_has_a_workspace_is_warned_that_accepting_makes_two()
    {
        using var harness = new AuthHarness();
        var principal = await MembershipPageTestHarness.SignedInAsync(harness, Guid.NewGuid(), WorkspaceRoles.Member, Xunit.TestContext.Current.CancellationToken);
        using var context = MembershipPageTestHarness.Create(harness, principal);
        context.Services.GetRequiredService<NavigationManager>().NavigateTo("/accept-invitation?token=abc");

        var cut = context.Render<AcceptInvitation>();

        Assert.Contains("makes two", cut.Find("[data-testid='accept-multiple-warning']").TextContent);
        Assert.Single(cut.FindAll("[data-testid='accept-form']"));
    }

    [Fact]
    public async Task A_user_with_no_workspace_gets_the_form_without_the_warning()
    {
        using var harness = new AuthHarness();
        var session = await harness.OpenSessionAsync(Xunit.TestContext.Current.CancellationToken);
        using var context = MembershipPageTestHarness.Create(harness, session.Principal);
        context.Services.GetRequiredService<NavigationManager>().NavigateTo("/accept-invitation?token=abc");

        var cut = context.Render<AcceptInvitation>();

        Assert.Empty(cut.FindAll("[data-testid='accept-multiple-warning']"));
        Assert.Single(cut.FindAll("[data-testid='accept-form']"));
    }
}
