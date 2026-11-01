using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using PostmanClone.TestKit;
using PostmanClone.Web.Api;

namespace PostmanClone.Web.Tests.Api;

/// <summary>
/// The agent client against a recording handler (WP31): the routes, the backend each call goes to, the bearer, and every failure as an
/// outcome. The recording keeps the method and the path, and the test checks the agent's own routes are never called from the server.
/// </summary>
public sealed class AgentClientTests
{
    private static CancellationToken Ct => Xunit.TestContext.Current.CancellationToken;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private sealed record Seen(string Backend, string Method, string PathAndQuery, string? Bearer);

    private static IAgentClient Client(Func<HttpRequestMessage, HttpResponseMessage> respond, List<Seen> seen)
    {
        var services = new ServiceCollection();
        services.AddTransient<BearerTokenHandler>();
        foreach (var backend in new[] { BackendServices.EnvironmentsName, BackendServices.ExecutionName })
        {
            services.AddHttpClient(backend, client => client.BaseAddress = new Uri($"http://{backend}-service"))
                .AddHttpMessageHandler<BearerTokenHandler>()
                .ConfigurePrimaryHttpMessageHandler(() => new FakeHttpMessageHandler(request =>
                {
                    seen.Add(new Seen(backend, request.Method.Method, request.RequestUri!.PathAndQuery, request.Headers.Authorization?.Parameter));
                    return respond(request);
                }));
        }

        return new AgentClient(services.BuildServiceProvider().GetRequiredService<IHttpClientFactory>());
    }

    private static HttpResponseMessage JsonBody(HttpStatusCode status, object body) =>
        new(status) { Content = JsonContent.Create(body, options: Json) };

    private static HttpResponseMessage Problem(HttpStatusCode status, string code) =>
        new(status) { Content = new StringContent($"{{\"title\":\"x\",\"status\":{(int)status},\"code\":\"{code}\"}}", System.Text.Encoding.UTF8, "application/problem+json") };

    [Fact]
    public async Task A_pairing_code_is_created_on_the_environments_service_with_the_callers_bearer()
    {
        var seen = new List<Seen>();
        var expires = DateTimeOffset.UtcNow.AddMinutes(5);
        var client = Client(_ => JsonBody(HttpStatusCode.Created, new PairingCodeDto("ABCD234567", expires)), seen);

        var answer = await client.CreatePairingCodeAsync("user-token", Ct);

        Assert.Equal(CollectionOutcome.Succeeded, answer.Outcome);
        Assert.Equal("ABCD234567", answer.Value!.Code);
        var call = Assert.Single(seen);
        Assert.Equal(BackendServices.EnvironmentsName, call.Backend);
        Assert.Equal("POST", call.Method);
        Assert.Equal("/api/v1/agent/pairing-codes", call.PathAndQuery);
        Assert.Equal("user-token", call.Bearer);
    }

    [Fact]
    public async Task Installations_are_listed_from_the_environments_service()
    {
        var seen = new List<Seen>();
        var page = new PageResult<AgentInstallationDto>(
            [new AgentInstallationDto(Guid.NewGuid(), "postmanclone-agent", "linux", "0.1.0", DateTimeOffset.UtcNow, null, DateTimeOffset.UtcNow.AddDays(90), null)],
            1, 100, 1);
        var client = Client(_ => JsonBody(HttpStatusCode.OK, page), seen);

        var answer = await client.ListInstallationsAsync("user-token", Ct);

        Assert.Single(answer.Value!.Items);
        Assert.Equal("GET", seen[0].Method);
        Assert.StartsWith("/api/v1/agent/installations", seen[0].PathAndQuery, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_revocation_of_an_agent_that_is_not_the_callers_is_not_found()
    {
        var seen = new List<Seen>();
        var client = Client(_ => Problem(HttpStatusCode.NotFound, "not_found"), seen);

        var answer = await client.RevokeInstallationAsync("user-token", Guid.NewGuid(), Ct);

        Assert.Equal(CollectionOutcome.NotFound, answer.Outcome);
        Assert.Equal("not_found", answer.Code);
        Assert.Equal("DELETE", seen[0].Method);
    }

    [Fact]
    public async Task A_run_is_recorded_on_the_execution_service_and_the_new_entry_is_returned()
    {
        var seen = new List<Seen>();
        var id = Guid.NewGuid();
        var client = Client(_ => JsonBody(HttpStatusCode.Created, new HistoryRecordedDto(id)), seen);
        var record = new HistoryRecordWriteDto(
            "agent",
            new RequestDefinition("GET", "http://localhost/x", null, null, null, null),
            null,
            null,
            new ExecutionResultDto("completed", null, null, null, new ExecutionTiming(1, 0, 0, 0, 0), "http://localhost/x", [], [], null));

        var answer = await client.RecordRunAsync("user-token", record, Ct);

        Assert.Equal(id, answer.Value!.Id);
        Assert.Equal(BackendServices.ExecutionName, seen[0].Backend);
        Assert.Equal("/api/v1/history/records", seen[0].PathAndQuery);
    }

    [Fact]
    public async Task No_answer_from_the_backend_is_unavailable_not_an_exception()
    {
        var seen = new List<Seen>();
        var client = Client(_ => throw new HttpRequestException("connection refused"), seen);

        var answer = await client.CreatePairingCodeAsync("user-token", Ct);

        Assert.Equal(CollectionOutcome.Unavailable, answer.Outcome);
        Assert.Null(answer.Value);
    }

    [Fact]
    public async Task The_server_never_calls_the_agents_own_routes()
    {
        // The agent's pair, send and resolve routes belong to the browser and the agent, never to the Web. Every operation is run and
        // each path it sends is checked, so a route added here would show up in the recording.
        var seen = new List<Seen>();
        var client = Client(request => request.Method == HttpMethod.Post && request.RequestUri!.AbsolutePath.EndsWith("/history/records", StringComparison.Ordinal)
            ? JsonBody(HttpStatusCode.Created, new HistoryRecordedDto(Guid.NewGuid()))
            : JsonBody(HttpStatusCode.OK, new PageResult<AgentInstallationDto>([], 1, 100, 0)), seen);
        var record = new HistoryRecordWriteDto("agent", new RequestDefinition("GET", "http://localhost/x", null, null, null, null), null, null,
            new ExecutionResultDto("completed", null, null, null, new ExecutionTiming(1, 0, 0, 0, 0), "http://localhost/x", [], [], null));

        await client.CreatePairingCodeAsync("user-token", Ct);
        await client.ListInstallationsAsync("user-token", Ct);
        await client.RevokeInstallationAsync("user-token", Guid.NewGuid(), Ct);
        await client.RecordRunAsync("user-token", record, Ct);

        Assert.Equal(4, seen.Count);
        Assert.All(seen, call =>
        {
            // The agent-only routes of EnvironmentsService (A2.4): the pairing exchange, the agent's identity and the resolve.
            var path = call.PathAndQuery.Split('?')[0];
            Assert.NotEqual("/api/v1/agent/pair", path);
            Assert.NotEqual("/api/v1/agent/me", path);
            Assert.DoesNotContain("/api/v1/agent/environments/", path, StringComparison.Ordinal);
        });
    }
}
