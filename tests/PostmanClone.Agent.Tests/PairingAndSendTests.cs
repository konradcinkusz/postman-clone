using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using PostmanClone.Agent.Config;
using PostmanClone.Agent.Remote;
using PostmanClone.Agent.Tests.Support;
using PostmanClone.Sending.Limits;
using PostmanClone.ExecutionService.Options;
using Xunit;

namespace PostmanClone.Agent.Tests;

/// <summary>
/// Pairing and sending through the real listener, against a real loopback target (WP30 tests, ADR-0026 Decisions 4 to 7).
/// The sentinel secret is the one value no response, log line or error may contain.
/// </summary>
public sealed class PairingAndSendTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "pc-agent-send-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Fact]
    public async Task Pairing_returns_a_local_token_and_stores_only_its_hash()
    {
        await using var agent = await RunningAgent.StartAsync(_directory);
        using var client = agent.Client();

        using var response = await PostJson(client, agent.Port, "/v1/pair", "{\"code\":\"ABCDEFGHJK\"}");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var json = JsonDocument.Parse(body);
        var pairingToken = json.RootElement.GetProperty("pairingToken").GetString()!;
        Assert.Equal(43, pairingToken.Length);
        var stored = new AgentConfigStore(_directory).Load();
        Assert.Equal(PairingTokens.Hash(pairingToken), stored.PairingTokenHash);
        Assert.DoesNotContain(pairingToken, File.ReadAllText(agent.Store.FilePath), StringComparison.Ordinal);
        Assert.Contains("agent-token-for-tests", File.ReadAllText(agent.Store.FilePath), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Pairing_takes_the_origins_from_the_server_when_it_names_some()
    {
        var server = new FakeServer
        {
            OnPair = (_, _, _, _, _) => new ServerReply<AgentPairingReply>(201, new AgentPairingReply(
                Guid.NewGuid(), "agent-token-for-tests-0000000000000000000", DateTimeOffset.UtcNow.AddDays(90), Guid.NewGuid(), ["https://other.example"]), null),
        };
        await using var agent = await RunningAgent.StartAsync(_directory, server);
        using var client = agent.Client();

        using var response = await PostJson(client, agent.Port, "/v1/pair", "{\"code\":\"ABCDEFGHJK\"}");

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        // The stored origins were the setup ones (Sentinels.Origin). The server's list replaces them.
        Assert.Equal(["https://other.example"], new AgentConfigStore(_directory).Load().AllowedWebOrigins);
    }

    [Fact]
    public async Task A_code_of_the_wrong_shape_is_refused_before_the_server_is_called()
    {
        await using var agent = await RunningAgent.StartAsync(_directory);
        using var client = agent.Client();

        using var response = await PostJson(client, agent.Port, "/v1/pair", "{\"code\":\"SHORT\"}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("pairing_code_invalid", await ReadCode(response));
    }

    [Fact]
    public async Task A_code_the_server_refuses_is_the_same_400_as_an_unknown_one()
    {
        var server = new FakeServer
        {
            OnPair = (_, _, _, _, _) => new ServerReply<AgentPairingReply>(400, null, "pairing_code_invalid"),
        };
        await using var agent = await RunningAgent.StartAsync(_directory, server);
        using var client = agent.Client();

        using var response = await PostJson(client, agent.Port, "/v1/pair", "{\"code\":\"ABCDEFGHJK\"}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("pairing_code_invalid", await ReadCode(response));
    }

    [Fact]
    public async Task A_second_pairing_replaces_the_first_local_token()
    {
        await using var agent = await RunningAgent.StartAsync(_directory);
        using var client = agent.Client();
        var first = await PairAndReadToken(client, agent.Port);
        var second = await PairAndReadToken(client, agent.Port);

        using var withFirst = await PostJson(client, agent.Port, "/v1/send", SendBody(), token: first);
        using var withSecond = await PostJson(client, agent.Port, "/v1/send", SendBody(), token: second);

        Assert.Equal(HttpStatusCode.Unauthorized, withFirst.StatusCode);
        Assert.Equal(HttpStatusCode.OK, withSecond.StatusCode);
    }

    [Fact]
    public async Task A_send_to_a_local_target_delivers_the_secret_and_no_response_or_log_contains_it()
    {
        // The core promise (ADR-0026 Decision 11): the secret reaches the target, and nothing the agent returns or writes has it.
        await using var target = await LoopbackTarget.StartAsync();
        await using var agent = await RunningAgent.StartAsync(_directory, paired: true);
        using var client = agent.Client();
        var environment = Guid.NewGuid();

        using var response = await PostJson(client, agent.Port, "/v1/send", SendBody(target.Port, environment, header: "{{token}}"), RunningAgent.PairingToken);
        var text = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(target.Received, header => header.Value == Sentinels.SecretValue);
        Assert.Equal("completed", ReadProperty(text, "outcome"));
        Assert.DoesNotContain(Sentinels.SecretValue, text, StringComparison.Ordinal);
        Assert.DoesNotContain(Sentinels.SecretValue, string.Join("\n", agent.Logs.Lines), StringComparison.Ordinal);
        Assert.Equal([environment], agent.Server.ResolvedEnvironments);
        Assert.Equal(["agent-token-for-tests-0000000000000000000"], agent.Server.ResolveTokens);
    }

    [Fact]
    public async Task A_send_to_the_agents_own_listener_is_refused_as_a_blocked_port()
    {
        // Threat: the agent used to reach its own listener, which would accept the request as if a page sent it.
        await using var agent = await RunningAgent.StartAsync(_directory, paired: true);
        using var client = agent.Client();

        using var response = await PostJson(client, agent.Port, "/v1/send", SendBody(agent.Port, null, url: $"http://127.0.0.1:{agent.Port}/v1/status"), RunningAgent.PairingToken);
        var text = await response.Content.ReadAsStringAsync();

        Assert.Equal("blocked", ReadProperty(text, "outcome"));
        Assert.Equal("ssrf_blocked_port", ReadProperty(text, "errorCode"));
    }

    [Fact]
    public async Task The_name_localhost_for_the_own_listener_is_refused_too()
    {
        await using var agent = await RunningAgent.StartAsync(_directory, paired: true);
        using var client = agent.Client();

        using var response = await PostJson(client, agent.Port, "/v1/send", SendBody(agent.Port, null, url: $"http://localhost:{agent.Port}/v1/status"), RunningAgent.PairingToken);

        Assert.Equal("ssrf_blocked_port", ReadProperty(await response.Content.ReadAsStringAsync(), "errorCode"));
    }

    [Fact]
    public async Task With_private_targets_off_a_loopback_target_is_refused_as_a_blocked_address()
    {
        await using var target = await LoopbackTarget.StartAsync();
        await using var agent = await RunningAgent.StartAsync(_directory, paired: true, allowPrivate: false);
        using var client = agent.Client();

        using var response = await PostJson(client, agent.Port, "/v1/send", SendBody(target.Port, null), RunningAgent.PairingToken);
        var text = await response.Content.ReadAsStringAsync();

        Assert.Equal("blocked", ReadProperty(text, "outcome"));
        Assert.Equal("ssrf_blocked_address", ReadProperty(text, "errorCode"));
        Assert.Empty(target.Received);
    }

    [Fact]
    public async Task By_default_a_loopback_target_is_reached()
    {
        // Local targets are the point of the agent (ADR-0026 Decision 6). The default allows them.
        await using var target = await LoopbackTarget.StartAsync();
        await using var agent = await RunningAgent.StartAsync(_directory, paired: true);
        using var client = agent.Client();

        using var response = await PostJson(client, agent.Port, "/v1/send", SendBody(target.Port, null), RunningAgent.PairingToken);
        var text = await response.Content.ReadAsStringAsync();

        Assert.Equal("completed", ReadProperty(text, "outcome"));
        Assert.Equal(200, JsonDocument.Parse(text).RootElement.GetProperty("response").GetProperty("statusCode").GetInt32());
    }

    [Fact]
    public async Task A_revoked_agent_token_answers_409_so_the_page_can_say_pair_again()
    {
        var server = new FakeServer
        {
            OnResolve = (_, _) => new ServerReply<ResolvedEnvironmentReply>(401, null, "agent_token_invalid"),
        };
        await using var agent = await RunningAgent.StartAsync(_directory, server, paired: true);
        using var client = agent.Client();

        using var response = await PostJson(client, agent.Port, "/v1/send", SendBody(47631, Guid.NewGuid()), RunningAgent.PairingToken);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("agent_not_paired", await ReadCode(response));
    }

    [Fact]
    public async Task An_environment_the_agent_cannot_resolve_is_one_answer_that_does_not_say_why()
    {
        var server = new FakeServer
        {
            OnResolve = (_, _) => new ServerReply<ResolvedEnvironmentReply>(404, null, "not_found"),
        };
        await using var agent = await RunningAgent.StartAsync(_directory, server, paired: true);
        using var client = agent.Client();

        using var response = await PostJson(client, agent.Port, "/v1/send", SendBody(47631, Guid.NewGuid()), RunningAgent.PairingToken);
        var text = await response.Content.ReadAsStringAsync();

        Assert.Equal("invalidRequest", ReadProperty(text, "outcome"));
        Assert.Equal("environment_unavailable", ReadProperty(text, "errorCode"));
        Assert.DoesNotContain("not found", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task A_body_above_the_one_megabyte_request_cap_is_refused_as_payload_too_large()
    {
        // The caps are the contract's (CONTRACTS §4): the same refusal ExecutionService gives.
        await using var agent = await RunningAgent.StartAsync(_directory, paired: true);
        using var client = agent.Client();
        var body = "{\"request\":{\"method\":\"POST\",\"url\":\"http://127.0.0.1:1/\",\"params\":[],\"headers\":[],\"body\":{\"type\":\"raw\",\"raw\":\""
            + new string('a', 1_100_000) + "\"}},\"timeoutSeconds\":5}";

        using var response = await PostJson(client, agent.Port, "/v1/send", body, RunningAgent.PairingToken);

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        Assert.Equal("payload_too_large", await ReadCode(response));
    }

    [Fact]
    public void The_agent_uses_the_same_caps_as_the_service_by_default()
    {
        // Threat: a result that differs by sender. The defaults are the contract's, and both senders read the same numbers.
        var fromService = new ExecutionOptions().ToLimits();

        Assert.Equal(SendingLimits.Contract, fromService);
        Assert.Equal(1_048_576, SendingLimits.Contract.RequestBodyMaxBytes);
        Assert.Equal(5L * 1024 * 1024, SendingLimits.Contract.ResponseReadCapBytes);
        Assert.Equal(60, SendingLimits.Contract.MaxTimeoutSeconds);
        Assert.Equal(5, SendingLimits.Contract.MaxRedirects);
    }

    private async Task<string> PairAndReadToken(HttpClient client, int port)
    {
        using var response = await PostJson(client, port, "/v1/pair", "{\"code\":\"ABCDEFGHJK\"}");
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.GetProperty("pairingToken").GetString()!;
    }

    private static string SendBody(int port = 47631, Guid? environment = null, string header = "plain", string? url = null) =>
        JsonSerializer.Serialize(new
        {
            request = new
            {
                method = "GET",
                url = url ?? $"http://127.0.0.1:{port}/probe",
                @params = Array.Empty<object>(),
                headers = new[] { new { key = "X-Demo", value = header, enabled = true } },
                body = new { type = "none", raw = (string?)null, rawLanguage = (string?)null, fields = Array.Empty<object>() },
                auth = new { type = "none" },
            },
            environmentId = environment,
            timeoutSeconds = 10,
            followRedirects = true,
        });

    private static async Task<HttpResponseMessage> PostJson(HttpClient client, int port, string path, string body, string? token = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, $"http://127.0.0.1:{port}{path}")
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
        request.Headers.Add("Origin", Sentinels.Origin);
        if (token is not null)
        {
            request.Headers.Add("X-Agent-Pairing-Token", token);
        }

        return await client.SendAsync(request);
    }

    private static string? ReadProperty(string json, string name)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    }

    private static async Task<string?> ReadCode(HttpResponseMessage response) => ReadProperty(await response.Content.ReadAsStringAsync(), "code");
}
