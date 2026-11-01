using Bunit;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using PostmanClone.TestKit;
using PostmanClone.ServiceDefaults.Authentication;
using PostmanClone.Web.Agent;
using PostmanClone.Web.Api;
using PostmanClone.Web.Auth;
using PostmanClone.Web.Components.Pages;
using PostmanClone.Web.Membership;
using PostmanClone.Web.Tests.Auth;
using PostmanClone.Web.Tests.Membership;

namespace PostmanClone.Web.Tests.Agent;

/// <summary>
/// The Agent page rendered by bUnit (WP31, ADR-0028): the code comes from its one-time ticket and leaves the address; the pairing is
/// the page's own call; the installations come with a revoke form that carries the antiforgery token; and a ticket of another session
/// gives nothing. The pairing token is in no markup. The agent and the server are fakes behind the real client code.
/// </summary>
public sealed class AgentPageTests
{
    private static CancellationToken Ct => Xunit.TestContext.Current.CancellationToken;

    private const string PairingSentinel = "pairing-token-sentinel-41c9"; // gitleaks:allow

    private sealed class FakeAgentClient(List<AgentInstallationDto> installations) : IAgentClient
    {
        public List<Guid> Revoked { get; } = [];

        public Task<CollectionAnswer<PairingCodeDto>> CreatePairingCodeAsync(string accessToken, CancellationToken cancellationToken) =>
            Task.FromResult(new CollectionAnswer<PairingCodeDto>(CollectionOutcome.Succeeded, new PairingCodeDto("ZZZZ234567", DateTimeOffset.UtcNow.AddMinutes(5)), null));

        public Task<CollectionAnswer<PageResult<AgentInstallationDto>>> ListInstallationsAsync(string accessToken, CancellationToken cancellationToken) =>
            Task.FromResult(new CollectionAnswer<PageResult<AgentInstallationDto>>(CollectionOutcome.Succeeded, new PageResult<AgentInstallationDto>(installations, 1, 100, installations.Count), null));

        public Task<CollectionAnswer> RevokeInstallationAsync(string accessToken, Guid installationId, CancellationToken cancellationToken)
        {
            Revoked.Add(installationId);
            return Task.FromResult(new CollectionAnswer(CollectionOutcome.Succeeded, null));
        }

        public Task<CollectionAnswer<PostmanClone.Web.Api.HistoryRecordedDto>> RecordRunAsync(string accessToken, HistoryRecordWriteDto record, CancellationToken cancellationToken) =>
            throw new NotSupportedException("The page does not record runs.");
    }

    private static AgentInstallationDto Installation(Guid id, DateTimeOffset? revoked = null) =>
        new(id, "postmanclone-agent", "linux", "0.1.0", DateTimeOffset.UtcNow.AddDays(-1), null, DateTimeOffset.UtcNow.AddDays(89), revoked);

    /// <summary>A signed-in circuit with the agent's services, the fake client and the page's navigation at <paramref name="path"/>.</summary>
    private static async Task<(BunitContext Context, AgentPairingTickets Tickets, FakeAgentClient Agents, string SessionId)> CircuitAsync(
        AuthHarness harness, string path, params AgentInstallationDto[] installations)
    {
        var principal = await MembershipPageTestHarness.SignedInAsync(harness, Guid.NewGuid(), WorkspaceRoles.Member, Ct);
        var context = MembershipPageTestHarness.Create(harness, principal);
        AgentTestRig.AddAgentServices(context);
        var tickets = new AgentPairingTickets(TimeProvider.System);
        var agents = new FakeAgentClient([.. installations]);
        context.Services.AddSingleton(tickets);
        context.Services.AddSingleton<IAgentClient>(agents);
        var sessionId = principal.FindFirst(ServerSession.SessionIdClaim)?.Value ?? throw new InvalidOperationException("The session has no id claim.");
        // The layout captures the session when the circuit starts; the page's calls need that capture to reach the token. It is
        // taken after the last registration, because resolving a service closes the registrations.
        await context.Services.GetRequiredService<WorkspaceState>().CaptureAsync(context.Services.GetRequiredService<AuthenticationStateProvider>());
        context.Services.GetRequiredService<NavigationManager>().NavigateTo(path);
        return (context, tickets, agents, sessionId);
    }

    [Fact]
    public async Task Without_a_ticket_the_page_offers_a_form_post_for_a_code_with_the_antiforgery_token()
    {
        using var harness = new AuthHarness();
        var (context, _, _, _) = await CircuitAsync(harness, "/agent");
        using (context)
        {
            var cut = context.Render<AgentPage>();

            var form = cut.Find("[data-testid=agent-create-code-form]");
            Assert.Equal("/agent/pairing-codes", form.GetAttribute("action"));
            Assert.Equal("post", form.GetAttribute("method"));
            Assert.Equal("test-token", form.QuerySelector("input[name=__RequestVerificationToken]")!.GetAttribute("value"));
        }
    }

    [Fact]
    public async Task A_ticket_gives_the_code_once_and_the_address_loses_the_ticket()
    {
        using var harness = new AuthHarness();
        var (context, tickets, _, sessionId) = await CircuitAsync(harness, "/agent");
        using (context)
        {
            var ticket = tickets.Issue(sessionId, "ABCD234567", DateTimeOffset.UtcNow.AddMinutes(5));
            context.Services.GetRequiredService<NavigationManager>().NavigateTo($"/agent?ticket={ticket}");

            var cut = context.Render<AgentPage>();

            cut.WaitForAssertion(() => Assert.Equal("ABCD234567", cut.Find("[data-testid=agent-pair-code]").TextContent));
            Assert.DoesNotContain("ticket=", context.Services.GetRequiredService<NavigationManager>().Uri, StringComparison.Ordinal);
            Assert.Null(tickets.Take(sessionId, ticket));
        }
    }

    [Fact]
    public async Task A_ticket_of_another_session_gives_no_code_and_says_so()
    {
        using var harness = new AuthHarness();
        var (context, tickets, _, _) = await CircuitAsync(harness, "/agent");
        using (context)
        {
            var ticket = tickets.Issue("another-session", "ABCD234567", DateTimeOffset.UtcNow.AddMinutes(5));
            context.Services.GetRequiredService<NavigationManager>().NavigateTo($"/agent?ticket={ticket}");

            var cut = context.Render<AgentPage>();

            cut.WaitForAssertion(() => Assert.Contains("no longer available", cut.Find("[data-testid=agent-pair-message]").TextContent, StringComparison.Ordinal));
            Assert.Empty(cut.FindAll("[data-testid=agent-pair-code]"));
        }
    }

    [Fact]
    public async Task Pairing_from_the_page_spends_the_code_and_the_token_is_in_no_markup()
    {
        using var harness = new AuthHarness();
        var (context, tickets, _, sessionId) = await CircuitAsync(harness, "/agent");
        using (context)
        {
            context.JSInterop.Setup<JsProbeReply>("agentClient.probe", _ => true).SetResult(new JsProbeReply("answered", "http://127.0.0.1:47631", false));
            context.JSInterop.Setup<JsHttpReply>("agentClient.pair", _ => true).SetResult(new JsHttpReply(201, $"{{\"pairingToken\":\"{PairingSentinel}\",\"agentId\":\"{Guid.NewGuid()}\"}}"));
            var ticket = tickets.Issue(sessionId, "ABCD234567", DateTimeOffset.UtcNow.AddMinutes(5));
            context.Services.GetRequiredService<NavigationManager>().NavigateTo($"/agent?ticket={ticket}");
            var cut = context.Render<AgentPage>();
            cut.WaitForAssertion(() => cut.Find("[data-testid=agent-pair]"));

            cut.Find("[data-testid=agent-pair]").Click();

            cut.WaitForAssertion(() => Assert.Equal("This page is paired with the agent. Sends can now go through it.", cut.Find("[data-testid=agent-pair-message]").TextContent));
            Assert.Empty(cut.FindAll("[data-testid=agent-pair-code]"));
            Assert.DoesNotContain(PairingSentinel, cut.Markup, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task The_installations_are_listed_and_each_revoke_form_posts_to_its_own_route_with_the_token()
    {
        using var harness = new AuthHarness();
        var id = Guid.NewGuid();
        var (context, _, _, _) = await CircuitAsync(harness, "/agent", Installation(id));
        using (context)
        {
            var cut = context.Render<AgentPage>();

            cut.WaitForAssertion(() => cut.Find($"[data-testid=agent-installation-{id}]"));
            var form = cut.Find($"[data-testid=agent-installation-{id}] form");
            Assert.Equal($"/agent/installations/{id}/revoke", form.GetAttribute("action"));
            Assert.Equal("test-token", form.QuerySelector("input[name=__RequestVerificationToken]")!.GetAttribute("value"));
        }
    }

    [Fact]
    public async Task A_revoked_installation_has_no_revoke_form()
    {
        using var harness = new AuthHarness();
        var id = Guid.NewGuid();
        var (context, _, _, _) = await CircuitAsync(harness, "/agent", Installation(id, DateTimeOffset.UtcNow.AddHours(-1)));
        using (context)
        {
            var cut = context.Render<AgentPage>();

            cut.WaitForAssertion(() => cut.Find($"[data-testid=agent-installation-{id}]"));
            Assert.Empty(cut.FindAll($"[data-testid=agent-installation-{id}] form"));
            Assert.Contains("Revoked", cut.Find($"[data-testid=agent-installation-{id}]").TextContent, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task The_revoke_notice_is_shown_from_the_query_and_names_no_token()
    {
        using var harness = new AuthHarness();
        var (context, _, _, _) = await CircuitAsync(harness, "/agent?notice=revoked");
        using (context)
        {
            var cut = context.Render<AgentPage>();

            Assert.Contains("was revoked", cut.Find("[data-testid=agent-notice]").TextContent, StringComparison.Ordinal);
        }
    }
}
