using System.Security.Claims;
using Bunit;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.Extensions.DependencyInjection;
using PostmanClone.Web.Api;
using PostmanClone.Web.Auth;
using PostmanClone.Web.Components.Layout;
using PostmanClone.Web.Tests.Auth;
using PostmanClone.Web.Tests.EnvironmentsUi;

namespace PostmanClone.Web.Tests.Components;

/// <summary>The layout renders the backend states, the navigation and the page body (bUnit, no browser).</summary>
public sealed class MainLayoutTests
{
    /// <summary>The services the layout needs: an anonymous test user, the workspace state, and an antiforgery token.</summary>
    private static BunitContext Context(IBackendStatusReader reader, AuthHarness harness)
    {
        var context = new BunitContext();
        context.Services.AddSingleton<AuthenticationStateProvider>(new AnonymousAuthentication());
        context.Services.AddScoped<WorkspaceState>();
        context.Services.AddSingleton<AntiforgeryStateProvider>(new StubAntiforgery());
        context.Services.AddSingleton(reader);
        PostmanClone.Web.Tests.Agent.AgentTestRig.AddAgentServices(context);
        EnvironmentsRig.AddSelectorServices(context.Services, harness);
        return context;
    }

    [Fact]
    public void The_status_strip_shows_each_backend_with_its_state()
    {
        using var harness = new AuthHarness();
        using var context = Context(new FixedStatusReader(
            new BackendStatus("authservice", "healthy"),
            new BackendStatus("collections", BackendStates.Unreachable)), harness);

        var cut = context.Render<MainLayout>(parameters => parameters.Add(p => p.Body, builder => builder.AddContent(0, "page body")));

        cut.WaitForState(() => cut.FindAll(".status-list li").Count == 2);
        Assert.Contains("authservice: healthy", cut.Find("[data-testid='status-authservice']").TextContent);
        Assert.Contains("badge-success", cut.Find("[data-testid='status-authservice'] span").ClassList);
        Assert.Contains("badge-danger", cut.Find("[data-testid='status-collections'] span").ClassList);
    }

    [Fact]
    public void The_navigation_renders_every_page_in_order_and_the_body_is_in_main()
    {
        using var harness = new AuthHarness();
        using var context = Context(new FixedStatusReader(), harness);

        var cut = context.Render<MainLayout>(parameters => parameters.Add(p => p.Body, builder => builder.AddContent(0, "page body")));

        var hrefs = cut.FindAll("nav a").Select(link => link.GetAttribute("href")).ToArray();
        Assert.Equal(["/", "/environments", "/history", "/import", "/workspace", "/about"], hrefs);
        Assert.Contains("page body", cut.Find("main#main").TextContent);
    }

    [Fact]
    public void A_signed_out_visitor_sees_no_sign_out_button()
    {
        using var harness = new AuthHarness();
        using var context = Context(new FixedStatusReader(), harness);

        var cut = context.Render<MainLayout>(parameters => parameters.Add(p => p.Body, builder => builder.AddContent(0, "page body")));

        Assert.Equal("Workspace: not signed in", cut.Find("[data-testid='workspace-badge']").TextContent);
        Assert.Empty(cut.FindAll("[data-testid='sign-out']"));
    }

    [Fact]
    public void A_failed_status_read_is_an_alert_not_an_exception()
    {
        using var harness = new AuthHarness();
        using var context = Context(new ThrowingStatusReader(), harness);

        var cut = context.Render<MainLayout>(parameters => parameters.Add(p => p.Body, builder => builder.AddContent(0, "page body")));

        cut.WaitForState(() => cut.FindAll("[role='alert']").Count == 1);
        Assert.Contains("could not be read", cut.Find("[role='alert']").TextContent);
    }

    private sealed class FixedStatusReader(params BackendStatus[] statuses) : IBackendStatusReader
    {
        public Task<IReadOnlyList<BackendStatus>> ReadAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<BackendStatus>>(statuses);
    }

    private sealed class ThrowingStatusReader : IBackendStatusReader
    {
        public Task<IReadOnlyList<BackendStatus>> ReadAsync(CancellationToken cancellationToken) =>
            throw new InvalidOperationException("simulated read failure");
    }

    /// <summary>An anonymous user, as a visitor before sign-in sees the layout.</summary>
    private sealed class AnonymousAuthentication : AuthenticationStateProvider
    {
        public override Task<AuthenticationState> GetAuthenticationStateAsync() =>
            Task.FromResult(new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity())));
    }

    private sealed class StubAntiforgery : AntiforgeryStateProvider
    {
        public override AntiforgeryRequestToken GetAntiforgeryToken() => new("test-token", "__RequestVerificationToken");
    }
}
