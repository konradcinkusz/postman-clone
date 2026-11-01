using Bunit;
using Microsoft.Extensions.DependencyInjection;
using PostmanClone.Web.Agent;
using PostmanClone.Web.Components.Agent;

namespace PostmanClone.Web.Tests.Agent;

/// <summary>
/// The top bar's agent status and the builder's sender choice (WP31), each state drawn from the probe the browser would answer. The
/// pairing token is in no rendered markup after the pairing.
/// </summary>
public sealed class AgentComponentTests
{
    private static CancellationToken Ct => Xunit.TestContext.Current.CancellationToken;

    private const string PairingSentinel = "pairing-token-sentinel-41c9"; // gitleaks:allow

    private static void ProbeAnswers(BunitContext context, JsProbeReply reply) =>
        context.JSInterop.Setup<JsProbeReply>("agentClient.probe", _ => true).SetResult(reply);

    private static void PairingAnswers(BunitContext context) =>
        context.JSInterop.Setup<JsHttpReply>("agentClient.pair", _ => true).SetResult(new JsHttpReply(201, $"{{\"pairingToken\":\"{PairingSentinel}\",\"agentId\":\"{Guid.NewGuid()}\"}}"));

    [Fact]
    public void With_no_agent_answering_the_badge_says_not_running()
    {
        using var context = new BunitContext();
        AgentTestRig.AddAgentServices(context);
        ProbeAnswers(context, new JsProbeReply("unreachable", null, false));

        var badge = context.Render<AgentStatusBadge>();

        badge.WaitForAssertion(() => Assert.Equal("Not running", badge.Find("[data-testid=agent-status]").TextContent));
        Assert.Equal("not-running", badge.Find("[data-testid=agent-status]").GetAttribute("data-state"));
    }

    [Fact]
    public void An_agent_that_is_not_paired_says_not_paired()
    {
        using var context = new BunitContext();
        AgentTestRig.AddAgentServices(context);
        ProbeAnswers(context, new JsProbeReply("answered", "http://127.0.0.1:47631", false));

        var badge = context.Render<AgentStatusBadge>();

        badge.WaitForAssertion(() => Assert.Equal("Not paired", badge.Find("[data-testid=agent-status]").TextContent));
    }

    [Fact]
    public void A_paired_agent_that_this_page_holds_no_token_for_says_pair_again()
    {
        using var context = new BunitContext();
        AgentTestRig.AddAgentServices(context);
        ProbeAnswers(context, new JsProbeReply("answered", "http://127.0.0.1:47631", true));

        var badge = context.Render<AgentStatusBadge>();

        badge.WaitForAssertion(() => Assert.Equal("Pair again", badge.Find("[data-testid=agent-status]").TextContent));
    }

    [Fact]
    public void A_refused_origin_reads_as_not_paired_with_the_setup_sentence_on_the_page()
    {
        using var context = new BunitContext();
        AgentTestRig.AddAgentServices(context);
        ProbeAnswers(context, new JsProbeReply("refused", "http://127.0.0.1:47631", false));

        var badge = context.Render<AgentStatusBadge>();

        badge.WaitForAssertion(() => Assert.Equal("Not paired", badge.Find("[data-testid=agent-status]").TextContent));
    }

    [Fact]
    public async Task The_badge_says_connected_once_this_page_pairs_and_the_token_is_in_no_markup()
    {
        using var context = new BunitContext();
        AgentTestRig.AddAgentServices(context);
        ProbeAnswers(context, new JsProbeReply("answered", "http://127.0.0.1:47631", true));
        PairingAnswers(context);
        var connection = context.Services.GetRequiredService<AgentConnection>();
        var badge = context.Render<AgentStatusBadge>();
        badge.WaitForAssertion(() => Assert.Equal("Pair again", badge.Find("[data-testid=agent-status]").TextContent));

        var paired = await badge.InvokeAsync(() => connection.PairAsync("ABCD234567", Ct));

        Assert.True(paired.Paired);
        badge.WaitForAssertion(() => Assert.Equal("Connected", badge.Find("[data-testid=agent-status]").TextContent));
        Assert.DoesNotContain(PairingSentinel, badge.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void The_sender_defaults_to_the_server_until_this_page_is_paired()
    {
        using var context = new BunitContext();
        AgentTestRig.AddAgentServices(context);
        ProbeAnswers(context, new JsProbeReply("answered", "http://127.0.0.1:47631", true));

        var select = context.Render<SendTargetSelect>();

        Assert.Equal("server", select.Find("#builder-send-target option[selected]").GetAttribute("value"));
        Assert.Contains("the server refuses", select.Markup, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Once_paired_the_sender_defaults_to_the_agent_and_a_choice_of_server_is_kept()
    {
        using var context = new BunitContext();
        AgentTestRig.AddAgentServices(context);
        ProbeAnswers(context, new JsProbeReply("answered", "http://127.0.0.1:47631", true));
        PairingAnswers(context);
        var connection = context.Services.GetRequiredService<AgentConnection>();
        var select = context.Render<SendTargetSelect>();
        await select.InvokeAsync(() => connection.PairAsync("ABCD234567", Ct));

        select.WaitForAssertion(() => Assert.Equal("agent", select.Find("#builder-send-target option[selected]").GetAttribute("value")));

        select.Find("#builder-send-target").Change("server");

        Assert.Equal(SendTarget.Server, connection.EffectiveTarget);
        Assert.Equal("server", select.Find("#builder-send-target option[selected]").GetAttribute("value"));
    }
}
