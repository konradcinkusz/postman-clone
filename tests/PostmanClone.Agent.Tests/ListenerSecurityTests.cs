using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using PostmanClone.Agent.Cli;
using PostmanClone.Agent.Config;
using PostmanClone.Agent.Local;
using PostmanClone.Agent.Tests.Support;
using Xunit;

namespace PostmanClone.Agent.Tests;

/// <summary>
/// The refusals of the listener, against a real Kestrel on a free loopback port (ADR-0026 threat model, CONTRACTS A2.6). Each
/// test names the threat it covers. A test that needs a token uses the token the fixture stored.
/// </summary>
public sealed class ListenerSecurityTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "pc-agent-tests-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Fact]
    public async Task A_foreign_origin_is_refused_and_gets_no_allow_header()
    {
        // Threat: a page on another site calls 127.0.0.1 (CORS).
        await using var agent = await RunningAgent.StartAsync(_directory);
        using var client = agent.Client();

        using var response = await Get(client, agent.Port, "/v1/status", "https://evil.example");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.False(response.Headers.Contains("Access-Control-Allow-Origin"));
        Assert.Equal("origin_not_allowed", await ReadCode(response));
    }

    [Theory]
    [InlineData("null")]
    [InlineData("https://app.example.evil.example")]
    [InlineData("https://app.example:8443")]
    [InlineData("https://app.example/")]
    public async Task A_null_or_prefix_or_port_variant_of_the_allowed_origin_is_refused(string origin)
    {
        await using var agent = await RunningAgent.StartAsync(_directory);
        using var client = agent.Client();

        using var response = await Get(client, agent.Port, "/v1/status", origin);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.False(response.Headers.Contains("Access-Control-Allow-Origin"));
    }

    [Fact]
    public async Task An_absent_origin_is_refused()
    {
        await using var agent = await RunningAgent.StartAsync(_directory);
        using var client = agent.Client();

        using var response = await client.GetAsync("/v1/status");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("origin_not_allowed", await ReadCode(response));
    }

    [Fact]
    public async Task The_allowed_origin_is_echoed_exactly_with_vary_and_no_credentials()
    {
        await using var agent = await RunningAgent.StartAsync(_directory);
        using var client = agent.Client();

        using var response = await Get(client, agent.Port, "/v1/status", Sentinels.Origin);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(Sentinels.Origin, response.Headers.GetValues("Access-Control-Allow-Origin").Single());
        Assert.Contains("Origin", response.Headers.GetValues("Vary"));
        Assert.False(response.Headers.Contains("Access-Control-Allow-Credentials"));
    }

    [Fact]
    public async Task A_rebinding_host_is_refused_even_with_an_allowed_origin()
    {
        // Threat: DNS rebinding. The page's own domain resolves to 127.0.0.1, so its Host is not the loopback name.
        await using var agent = await RunningAgent.StartAsync(_directory);
        using var client = agent.Client();

        using var request = new HttpRequestMessage(HttpMethod.Get, "/v1/status");
        request.Headers.Host = $"evil.example:{agent.Port}";
        request.Headers.Add("Origin", Sentinels.Origin);
        using var response = await client.SendAsync(request);

        Assert.Equal((HttpStatusCode)421, response.StatusCode);
        Assert.Equal("host_not_allowed", await ReadCode(response));
    }

    [Fact]
    public async Task The_loopback_name_localhost_is_an_accepted_host()
    {
        await using var agent = await RunningAgent.StartAsync(_directory);
        using var client = agent.Client();

        using var request = new HttpRequestMessage(HttpMethod.Get, "/v1/status");
        request.Headers.Host = $"localhost:{agent.Port}";
        request.Headers.Add("Origin", Sentinels.Origin);
        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Status_answers_the_name_protocol_and_paired_flag_with_no_token()
    {
        await using var agent = await RunningAgent.StartAsync(_directory);
        using var client = agent.Client();

        using var response = await Get(client, agent.Port, "/v1/status", Sentinels.Origin);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        Assert.Equal("postmanclone-agent", json.RootElement.GetProperty("agent").GetString());
        Assert.Equal(1, json.RootElement.GetProperty("protocol").GetInt32());
        Assert.False(json.RootElement.GetProperty("paired").GetBoolean());
        Assert.DoesNotContain("Token", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_preflight_with_private_network_gets_the_header_only_for_the_allowed_origin()
    {
        // Threat: Private Network Access. The header is the browser's permission to reach a private address from a public page.
        await using var agent = await RunningAgent.StartAsync(_directory);
        using var client = agent.Client();

        using var allowed = Preflight(agent.Port, Sentinels.Origin);
        using var allowedResponse = await client.SendAsync(allowed);
        Assert.Equal(HttpStatusCode.NoContent, allowedResponse.StatusCode);
        Assert.Equal("true", allowedResponse.Headers.GetValues("Access-Control-Allow-Private-Network").Single());
        Assert.Equal(Sentinels.Origin, allowedResponse.Headers.GetValues("Access-Control-Allow-Origin").Single());
        Assert.Contains("X-Agent-Pairing-Token", allowedResponse.Headers.GetValues("Access-Control-Allow-Headers").Single());

        using var foreign = Preflight(agent.Port, "https://evil.example");
        using var foreignResponse = await client.SendAsync(foreign);
        Assert.Equal(HttpStatusCode.Forbidden, foreignResponse.StatusCode);
        Assert.False(foreignResponse.Headers.Contains("Access-Control-Allow-Private-Network"));
        Assert.False(foreignResponse.Headers.Contains("Access-Control-Allow-Origin"));
    }

    [Fact]
    public async Task A_send_without_the_pairing_token_is_refused_with_401()
    {
        // Threat: a "simple" cross-site request that reaches the handler without the custom header.
        await using var agent = await RunningAgent.StartAsync(_directory, paired: true);
        using var client = agent.Client();

        using var response = await PostJson(client, agent.Port, "/v1/send", Sentinels.Origin, "{\"request\":{}}", token: null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("agent_token_invalid", await ReadCode(response));
    }

    [Fact]
    public async Task A_send_with_a_wrong_pairing_token_is_refused_with_401()
    {
        await using var agent = await RunningAgent.StartAsync(_directory, paired: true);
        using var client = agent.Client();

        using var response = await PostJson(client, agent.Port, "/v1/send", Sentinels.Origin, "{\"request\":{}}", token: "not-the-token");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task A_send_with_text_plain_is_refused_with_415_even_with_the_token()
    {
        // Threat: a form or text body that a browser can send without a preflight.
        await using var agent = await RunningAgent.StartAsync(_directory, paired: true);
        using var client = agent.Client();

        using var response = await PostText(client, agent.Port, "/v1/send", Sentinels.Origin, "{}", RunningAgent.PairingToken);

        Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
    }

    [Fact]
    public async Task A_pair_with_text_plain_is_refused_with_415()
    {
        await using var agent = await RunningAgent.StartAsync(_directory);
        using var client = agent.Client();

        using var response = await PostText(client, agent.Port, "/v1/pair", Sentinels.Origin, "{\"code\":\"ABCDEFGHJK\"}", token: null);

        Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
        Assert.Empty(agent.Server.ResolveTokens);
    }

    [Fact]
    public async Task A_body_over_two_megabytes_is_refused_with_413()
    {
        await using var agent = await RunningAgent.StartAsync(_directory, paired: true);
        using var client = agent.Client();
        var large = "{\"request\":{\"method\":\"GET\",\"url\":\"http://127.0.0.1/\"},\"pad\":\"" + new string('x', 2 * 1024 * 1024 + 10) + "\"}";

        // Expect: 100-continue makes the server answer before the body is sent, so the 413 is not raced against a reset.
        using var request = new HttpRequestMessage(HttpMethod.Post, $"http://127.0.0.1:{agent.Port}/v1/send")
        {
            Content = new StringContent(large, Encoding.UTF8, "application/json"),
            Headers = { ExpectContinue = true },
        };
        request.Headers.Add("Origin", Sentinels.Origin);
        request.Headers.Add("X-Agent-Pairing-Token", RunningAgent.PairingToken);
        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
    }

    [Fact]
    public async Task No_response_on_the_listener_carries_a_wildcard_origin()
    {
        // Threat: a CORS answer that lets any page read the response. Every route and every refusal is checked.
        await using var agent = await RunningAgent.StartAsync(_directory, paired: true);
        using var client = agent.Client();
        var answers = new List<HttpResponseMessage>
        {
            await Get(client, agent.Port, "/v1/status", Sentinels.Origin),
            await Get(client, agent.Port, "/v1/status", "https://evil.example"),
            await client.SendAsync(Preflight(agent.Port, Sentinels.Origin)),
            await client.SendAsync(Preflight(agent.Port, "https://evil.example")),
            await PostJson(client, agent.Port, "/v1/send", Sentinels.Origin, "{}", token: null),
            await PostJson(client, agent.Port, "/v1/pair", "https://evil.example", "{}", token: null),
        };

        try
        {
            foreach (var answer in answers)
            {
                Assert.False(answer.Headers.TryGetValues("Access-Control-Allow-Origin", out var values) && values.Contains("*"));
                Assert.False(answer.Headers.Contains("Access-Control-Allow-Credentials"));
            }
        }
        finally
        {
            foreach (var answer in answers)
            {
                answer.Dispose();
            }
        }
    }

    [Fact]
    public async Task The_listener_is_bound_to_the_loopback_address_only()
    {
        // Threat: the agent reachable from the network. The bound addresses are read from the running server.
        await using var agent = await RunningAgent.StartAsync(_directory);

        var addresses = agent.App.Services.GetRequiredService<Microsoft.AspNetCore.Hosting.Server.IServer>()
            .Features.Get<Microsoft.AspNetCore.Hosting.Server.Features.IServerAddressesFeature>()!.Addresses.ToList();

        var single = Assert.Single(addresses);
        Assert.Equal($"http://127.0.0.1:{agent.Port}", single);
    }

    [Fact]
    public async Task A_second_agent_on_the_same_port_exits_non_zero()
    {
        // Threat: a second process takes the port and answers in its place. The second start must refuse.
        await using var agent = await RunningAgent.StartAsync(_directory);
        var store = new AgentConfigStore(Path.Combine(_directory, "second"));

        var exitCode = await AgentCli.RunAsync(["run", "--port", agent.Port.ToString()], store, new FakeServer());

        Assert.Equal(1, exitCode);
    }

    [Fact]
    public async Task The_secret_free_status_names_the_agent_and_no_pairing_material()
    {
        await using var agent = await RunningAgent.StartAsync(_directory, paired: true);
        using var client = agent.Client();

        using var response = await Get(client, agent.Port, "/v1/status", Sentinels.Origin);
        var text = await response.Content.ReadAsStringAsync();

        Assert.Contains("\"paired\":true", text, StringComparison.Ordinal);
        Assert.DoesNotContain(RunningAgent.PairingToken, text, StringComparison.Ordinal);
        Assert.DoesNotContain("agent-token-for-tests", text, StringComparison.Ordinal);
    }

    private static HttpRequestMessage Preflight(int port, string origin)
    {
        var request = new HttpRequestMessage(HttpMethod.Options, $"http://127.0.0.1:{port}/v1/send");
        request.Headers.Add("Origin", origin);
        request.Headers.Add("Access-Control-Request-Method", "POST");
        request.Headers.Add("Access-Control-Request-Headers", "content-type, x-agent-pairing-token");
        request.Headers.Add("Access-Control-Request-Private-Network", "true");
        return request;
    }

    private static Task<HttpResponseMessage> Get(HttpClient client, int port, string path, string origin)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, $"http://127.0.0.1:{port}{path}");
        request.Headers.Add("Origin", origin);
        return client.SendAsync(request);
    }

    private static Task<HttpResponseMessage> PostJson(HttpClient client, int port, string path, string origin, string body, string? token) =>
        PostText(client, port, path, origin, body, token, "application/json");

    private static Task<HttpResponseMessage> PostText(HttpClient client, int port, string path, string origin, string body, string? token, string contentType = "text/plain")
    {
        var request = new HttpRequestMessage(HttpMethod.Post, $"http://127.0.0.1:{port}{path}")
        {
            Content = new StringContent(body, Encoding.UTF8, contentType),
        };
        request.Headers.Add("Origin", origin);
        if (token is not null)
        {
            request.Headers.Add("X-Agent-Pairing-Token", token);
        }

        return client.SendAsync(request);
    }

    private static async Task<string?> ReadCode(HttpResponseMessage response)
    {
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.TryGetProperty("code", out var code) ? code.GetString() : null;
    }
}
