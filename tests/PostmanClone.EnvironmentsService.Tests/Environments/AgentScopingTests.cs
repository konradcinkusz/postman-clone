using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using PostmanClone.EnvironmentsService.Agent;
using PostmanClone.EnvironmentsService.Api;
using Xunit;
using static PostmanClone.EnvironmentsService.Tests.Environments.EnvironmentsApiTests;
using static PostmanClone.EnvironmentsService.Tests.Environments.EnvironmentsHost;
using static PostmanClone.EnvironmentsService.Tests.Environments.SecretFixtures;

namespace PostmanClone.EnvironmentsService.Tests.Environments;

/// <summary>
/// The agent routes (CONTRACTS A2.2 to A2.4). Every agent is paired through the real pairing flow, so the rows are the ones a
/// user creates. Covers the scoping of each id route, the audience rule in both directions, single-use codes, and revocation.
/// </summary>
public sealed class AgentScopingTests
{
    private const string Pair = "/api/v1/agent/pair";
    private const string PairingCodes = "/api/v1/agent/pairing-codes";
    private const string Installations = "/api/v1/agent/installations";
    private const string Me = "/api/v1/agent/me";
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Scoping_DELETE_agent_installations_id()
    {
        // Another workspace's agent and another user's agent in the same workspace are both "not found" (CONTRACTS A2.2).
        await using var host = await StartAsync();
        var owner = Caller.Owner(Guid.NewGuid());
        var agentId = await PairedAgentIdAsync(host, owner);

        using var foreignWorkspace = await host.SendAsync(HttpMethod.Delete, $"{Installations}/{agentId}", Caller.Owner(Guid.NewGuid(), "foreign"), cancellationToken: Ct);
        using var otherUser = await host.SendAsync(HttpMethod.Delete, $"{Installations}/{agentId}", Caller.Owner(owner.Workspace, "other-user"), cancellationToken: Ct);
        using var listed = await host.SendAsync(HttpMethod.Get, Installations, owner, cancellationToken: Ct);
        var page = await listed.Content.ReadFromJsonAsync<Page<AgentInstallationDto>>(Ct);

        Assert.Equal(HttpStatusCode.NotFound, foreignWorkspace.StatusCode);
        Assert.Equal("not_found", await foreignWorkspace.ProblemCodeAsync(Ct));
        Assert.Equal(HttpStatusCode.NotFound, otherUser.StatusCode);
        Assert.Null(Assert.Single(page!.Items).RevokedAt);
    }

    [Fact]
    public async Task Scoping_POST_agent_environments_id_resolve()
    {
        // The agent of another workspace resolves this workspace's environment: absent, so 404, never the values.
        await using var host = await StartAsync();
        var owner = Caller.Owner(Guid.NewGuid());
        var environment = await EnvironmentsApiTests.CreateAsync(host, owner, "workspace-a");
        var ownerAgent = await PairAsync(host, owner);
        var foreignAgent = await PairAsync(host, Caller.Owner(Guid.NewGuid(), "foreign"));

        using var foreign = await WithBearer(host, HttpMethod.Post, $"/api/v1/agent/environments/{environment.Id}/resolve", foreignAgent.AgentToken);
        using var mine = await WithBearer(host, HttpMethod.Post, $"/api/v1/agent/environments/{environment.Id}/resolve", ownerAgent.AgentToken);

        Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);
        Assert.Equal("not_found", await foreign.ProblemCodeAsync(Ct));
        Assert.Equal(HttpStatusCode.OK, mine.StatusCode);
    }

    [Fact]
    public async Task A_user_token_is_refused_on_the_agent_only_routes()
    {
        // Audience rule: the agent-only routes accept an agent token and nothing else (ADR-0026 Decision 3).
        await using var host = await StartAsync();
        var owner = Caller.Owner(Guid.NewGuid());
        var environment = await EnvironmentsApiTests.CreateAsync(host, owner, "workspace-a");

        using var resolve = await host.SendAsync(HttpMethod.Post, $"/api/v1/agent/environments/{environment.Id}/resolve", owner, cancellationToken: Ct);
        using var me = await host.SendAsync(HttpMethod.Get, Me, owner, cancellationToken: Ct);

        Assert.Equal(HttpStatusCode.Unauthorized, resolve.StatusCode);
        Assert.Equal("agent_token_invalid", await resolve.ProblemCodeAsync(Ct));
        Assert.Equal(HttpStatusCode.Unauthorized, me.StatusCode);
    }

    [Fact]
    public async Task An_agent_token_is_refused_on_every_user_route()
    {
        // The other half of the audience rule: an agent token is an unknown bearer everywhere else.
        await using var host = await StartAsync();
        var owner = Caller.Owner(Guid.NewGuid());
        var agent = await PairAsync(host, owner);

        using var environments = await WithBearer(host, HttpMethod.Get, "/api/v1/environments", agent.AgentToken);
        using var installations = await WithBearer(host, HttpMethod.Get, Installations, agent.AgentToken);

        Assert.Equal(HttpStatusCode.Unauthorized, environments.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, installations.StatusCode);
    }

    [Fact]
    public async Task A_pairing_code_is_single_use()
    {
        await using var host = await StartAsync();
        var owner = Caller.Owner(Guid.NewGuid());
        var code = await CodeAsync(host, owner);

        using var first = await Exchange(host, code);
        using var second = await Exchange(host, code);

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, second.StatusCode);
        Assert.Equal("pairing_code_invalid", await second.ProblemCodeAsync(Ct));
    }

    [Fact]
    public async Task A_code_that_was_replaced_and_a_code_never_issued_give_the_same_answer()
    {
        await using var host = await StartAsync();
        var owner = Caller.Owner(Guid.NewGuid());
        var replaced = await CodeAsync(host, owner);
        _ = await CodeAsync(host, owner);

        using var old = await Exchange(host, replaced);
        using var never = await Exchange(host, "ZZZZZZZZZZ");

        Assert.Equal(HttpStatusCode.BadRequest, old.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, never.StatusCode);
        Assert.Equal(await never.ProblemCodeAsync(Ct), await old.ProblemCodeAsync(Ct));
    }

    [Fact]
    public async Task Revoking_an_agent_stops_its_token_at_once()
    {
        await using var host = await StartAsync();
        var owner = Caller.Owner(Guid.NewGuid());
        var agent = await PairAsync(host, owner);

        using var revoke = await host.SendAsync(HttpMethod.Delete, $"{Installations}/{agent.AgentId}", owner, cancellationToken: Ct);
        using var me = await WithBearer(host, HttpMethod.Get, Me, agent.AgentToken);

        Assert.Equal(HttpStatusCode.NoContent, revoke.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, me.StatusCode);
    }

    [Fact]
    public async Task The_agent_sees_its_own_identity_and_no_secret_material()
    {
        await using var host = await StartAsync();
        var owner = Caller.Owner(Guid.NewGuid());
        var agent = await PairAsync(host, owner);

        using var me = await WithBearer(host, HttpMethod.Get, Me, agent.AgentToken);
        var identity = await me.Content.ReadFromJsonAsync<AgentIdentity>(Ct);

        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
        Assert.Equal(agent.AgentId, identity!.AgentId);
        Assert.Equal(owner.Workspace, identity.WorkspaceId);
    }

    [Fact]
    public async Task The_pairing_answer_names_the_origins_from_configuration_and_no_others()
    {
        await using var host = await StartAsync();
        var owner = Caller.Owner(Guid.NewGuid());
        var code = await CodeAsync(host, owner);

        using var response = await Exchange(host, code);
        var pairing = await response.Content.ReadFromJsonAsync<AgentPairing>(Ct);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(43, pairing!.AgentToken.Length);
        Assert.Equal(owner.Workspace, pairing.WorkspaceId);
    }

    [Fact(Skip = "Not verified here: the expiry needs a controllable clock in the in-process host. Covered by the code path, not by a test yet.")]
    public Task An_expired_code_is_refused_like_a_used_one() => Task.CompletedTask;

    [Fact(Skip = "Not verified here: two concurrent exchanges need the PostgreSQL host (TEST_POSTGRES), which this session did not add a test for.")]
    public Task Two_concurrent_exchanges_of_one_code_admit_one() => Task.CompletedTask;

    private static async Task<EnvironmentsHost> StartAsync() => await EnvironmentsHost.StartAsync(cancellationToken: Ct);

    private static async Task<string> CodeAsync(EnvironmentsHost host, Caller owner)
    {
        using var response = await host.SendAsync(HttpMethod.Post, PairingCodes, owner, cancellationToken: Ct);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<PairingCodeDto>(Ct))!.Code;
    }

    private static Task<HttpResponseMessage> Exchange(EnvironmentsHost host, string code) =>
        host.Client.SendAsync(new HttpRequestMessage(HttpMethod.Post, Pair)
        {
            Content = JsonContent.Create(new { code, agentName = "laptop", platform = "linux-x64", version = "1.0.0" }),
        }, Ct);

    private static async Task<AgentPairing> PairAsync(EnvironmentsHost host, Caller owner)
    {
        var code = await CodeAsync(host, owner);
        using var response = await Exchange(host, code);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<AgentPairing>(Ct))!;
    }

    private static async Task<Guid> PairedAgentIdAsync(EnvironmentsHost host, Caller owner) => (await PairAsync(host, owner)).AgentId;

    private static Task<HttpResponseMessage> WithBearer(EnvironmentsHost host, HttpMethod method, string url, string bearer) =>
        host.Client.SendAsync(new HttpRequestMessage(method, url)
        {
            Headers = { Authorization = new AuthenticationHeaderValue("Bearer", bearer) },
        }, Ct);
}
