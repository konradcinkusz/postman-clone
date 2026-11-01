using Bunit;
using Microsoft.Extensions.DependencyInjection;
using PostmanClone.Web.Agent;
using PostmanClone.Web.Api;
using PostmanClone.Web.Components.Response;
using PostmanClone.Web.Response;

namespace PostmanClone.Web.Tests.Agent;

/// <summary>
/// The send path with the agent (WP31): the default sender, the choice the user makes, the server path unchanged, the sender the
/// response names, and the sentinel secret and the pairing token in no rendered markup after a pairing, a send or a failed send.
/// The connection is the real one; only the browser's calls are the bUnit interop's answers.
/// </summary>
public sealed class AgentSendTests
{
    private static CancellationToken Ct => Xunit.TestContext.Current.CancellationToken;

    // Fixtures that must look like secrets (CONTRACTS §4): they stand in for a resolved value and a pairing token.
    private const string SecretSentinel = "sentinel-secret-value-7f3a"; // gitleaks:allow
    private const string PairingSentinel = "pairing-token-sentinel-41c9"; // gitleaks:allow

    private static readonly RequestDefinition Request = new("GET", "http://localhost/x", null, null, null, null);

    private sealed class ScriptedGateway : IExecutionGateway
    {
        public int Calls { get; private set; }

        public Task<CollectionAnswer<ExecutionResultDto>> ExecuteAsync(ExecuteBody body, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(new CollectionAnswer<ExecutionResultDto>(CollectionOutcome.Succeeded, Result(), null));
        }
    }

    private sealed class FakeRunner(AgentRunAnswer answer) : IAgentRunner
    {
        public List<ExecuteBody> Sent { get; } = [];

        public Task<AgentRunAnswer> SendAsync(ExecuteBody body, CancellationToken cancellationToken)
        {
            Sent.Add(body);
            return Task.FromResult(answer);
        }
    }

    private static ExecutionResultDto Result(Guid? historyId = null) =>
        new("completed", null, null, new ResponseDto(200, "OK", "HTTP/1.1", [new HeaderLine("Content-Type", "application/json")],
            new ResponseBodyDto("utf8", "{\"ok\":true}", 11, false, "application/json")),
            new ExecutionTiming(12, 1, 2, 3, 4), "http://localhost/x", [], [], historyId);

    /// <summary>A circuit whose agent answers as paired, and whose page has paired with it (the real code paths).</summary>
    private static async Task<AgentConnection> PairedAsync(BunitContext context)
    {
        context.JSInterop.Setup<JsProbeReply>("agentClient.probe", _ => true).SetResult(new JsProbeReply("answered", "http://127.0.0.1:47631", true));
        context.JSInterop.Setup<JsHttpReply>("agentClient.pair", _ => true).SetResult(new JsHttpReply(201, $"{{\"pairingToken\":\"{PairingSentinel}\",\"agentId\":\"{Guid.NewGuid()}\"}}"));
        var connection = context.Services.GetRequiredService<AgentConnection>();
        await connection.ProbeAsync(Ct);
        var paired = await connection.PairAsync("ABCD234567", Ct);
        Assert.True(paired.Paired);
        return connection;
    }

    [Fact]
    public async Task A_paired_page_sends_through_the_agent_by_default_and_the_response_says_so()
    {
        using var context = new BunitContext();
        AgentTestRig.AddAgentServices(context);
        var connection = await PairedAsync(context);
        var gateway = new ScriptedGateway();
        var runner = new FakeRunner(new AgentRunAnswer(Result(Guid.NewGuid()), null));
        var coordinator = new SendCoordinator(gateway, new EnvironmentSelection(), connection, runner);

        await coordinator.SendAsync(Guid.NewGuid(), Request, null, Ct);

        Assert.Equal(SendTarget.Agent, connection.EffectiveTarget);
        Assert.Single(runner.Sent);
        Assert.Equal(0, gateway.Calls);
        Assert.Equal(SendPhase.Shown, coordinator.View.Phase);
        Assert.Equal(SendSender.Agent, coordinator.View.Sender);
    }

    [Fact]
    public async Task An_unpaired_page_sends_through_the_server_and_the_agent_is_not_called()
    {
        using var context = new BunitContext();
        AgentTestRig.AddAgentServices(context);
        var connection = context.Services.GetRequiredService<AgentConnection>();
        var gateway = new ScriptedGateway();
        var runner = new FakeRunner(new AgentRunAnswer(null, AgentMessages.NotPaired));
        var coordinator = new SendCoordinator(gateway, new EnvironmentSelection(), connection, runner);

        await coordinator.SendAsync(Guid.NewGuid(), Request, null, Ct);

        Assert.Equal(SendTarget.Server, connection.EffectiveTarget);
        Assert.Empty(runner.Sent);
        Assert.Equal(1, gateway.Calls);
        Assert.Equal(SendSender.Server, coordinator.View.Sender);
    }

    [Fact]
    public async Task A_choice_of_the_server_is_kept_even_when_the_page_is_paired()
    {
        using var context = new BunitContext();
        AgentTestRig.AddAgentServices(context);
        var connection = await PairedAsync(context);
        connection.Choose(SendTarget.Server);
        var gateway = new ScriptedGateway();
        var runner = new FakeRunner(new AgentRunAnswer(null, AgentMessages.NotRunning));
        var coordinator = new SendCoordinator(gateway, new EnvironmentSelection(), connection, runner);

        await coordinator.SendAsync(Guid.NewGuid(), Request, null, Ct);

        Assert.Empty(runner.Sent);
        Assert.Equal(1, gateway.Calls);
        Assert.Equal(SendPhase.Shown, coordinator.View.Phase);
    }

    [Fact]
    public async Task A_refused_agent_send_shows_its_sentence_and_no_secret_or_token_in_the_panel()
    {
        using var context = new BunitContext();
        AgentTestRig.AddAgentServices(context);
        var connection = await PairedAsync(context);
        var runner = new FakeRunner(new AgentRunAnswer(null, AgentMessages.PairAgain));
        var coordinator = new SendCoordinator(new ScriptedGateway(), new EnvironmentSelection(), connection, runner);

        await coordinator.SendAsync(Guid.NewGuid(), Request, null, Ct);
        var panel = context.Render<ResponsePanel>(parameters => parameters.Add(p => p.View, coordinator.View));

        Assert.Equal(SendPhase.Failed, coordinator.View.Phase);
        Assert.Equal(SendSender.Agent, coordinator.View.Sender);
        Assert.Contains(AgentMessages.PairAgain, panel.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain(PairingSentinel, panel.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain(SecretSentinel, panel.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_successful_agent_send_renders_only_the_masked_value_and_no_token()
    {
        using var context = new BunitContext();
        AgentTestRig.AddAgentServices(context);
        var connection = await PairedAsync(context);
        // The agent returns the masked result (CONTRACTS §4): the sentinel never reaches the Web.
        var masked = Result() with { Response = Result().Response! with { Body = new ResponseBodyDto("utf8", "{\"auth\":\"••••••\"}", 17, false, "application/json") } };
        var runner = new FakeRunner(new AgentRunAnswer(masked, null));
        var coordinator = new SendCoordinator(new ScriptedGateway(), new EnvironmentSelection(), connection, runner);

        await coordinator.SendAsync(Guid.NewGuid(), Request, null, Ct);
        var panel = context.Render<ResponsePanel>(parameters => parameters.Add(p => p.View, coordinator.View));

        Assert.Contains("••••••", panel.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain(SecretSentinel, panel.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain(PairingSentinel, panel.Markup, StringComparison.Ordinal);
    }
}
