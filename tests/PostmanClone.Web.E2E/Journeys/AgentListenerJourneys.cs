using System.Net;
using System.Text.Json;
using PostmanClone.Web.E2E.Support;

namespace PostmanClone.Web.E2E.Journeys;

/// <summary>
/// The agent's listener, tested with raw HTTP against the real process (WP32, CONTRACTS A2.6, ADR-0026 Decision 5). A browser
/// is not needed: each request states its own headers, so the test can send what a hostile page or another host would send.
/// Every refusal must come before any work, and a refused request gets no allow header a page could read.
/// </summary>
public sealed class AgentListenerJourneys
{
    private const string ForeignOrigin = "https://evil.example";
    private const string Token = "X-Agent-Pairing-Token";

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(10) };

    private static Uri StatusUri => new(AgentStack.BaseAddress, "/v1/status");

    private static Uri SendUri => new(AgentStack.BaseAddress, "/v1/send");

    private static string WebOrigin => AgentStack.WebOrigin;

    [Fact]
    public async Task A_foreign_origin_is_refused_and_no_allow_header_is_set()
    {
        await AgentStack.RequireAsync();
        using var request = new HttpRequestMessage(HttpMethod.Get, StatusUri);
        request.Headers.TryAddWithoutValidation("Origin", ForeignOrigin);

        using var response = await Http.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("origin_not_allowed", await CodeAsync(response));
        Assert.False(response.Headers.Contains("Access-Control-Allow-Origin"));
    }

    [Fact]
    public async Task A_request_with_no_origin_is_refused()
    {
        await AgentStack.RequireAsync();
        using var request = new HttpRequestMessage(HttpMethod.Get, StatusUri);

        using var response = await Http.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("origin_not_allowed", await CodeAsync(response));
    }

    [Fact]
    public async Task A_rebinding_host_name_is_refused_with_421_even_from_the_allowed_origin()
    {
        await AgentStack.RequireAsync();
        using var request = new HttpRequestMessage(HttpMethod.Get, StatusUri);
        request.Headers.TryAddWithoutValidation("Origin", WebOrigin);
        request.Headers.Host = $"evil.example:{AgentStack.Port}";

        using var response = await Http.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.MisdirectedRequest, response.StatusCode);
        Assert.Equal("host_not_allowed", await CodeAsync(response));
    }

    [Fact]
    public async Task A_send_without_the_pairing_token_is_refused_with_401()
    {
        await AgentStack.RequireAsync();
        using var response = await PostSendAsync(WebOrigin, token: null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("agent_token_invalid", await CodeAsync(response));
    }

    [Fact]
    public async Task A_send_with_a_wrong_pairing_token_is_refused_with_401()
    {
        await AgentStack.RequireAsync();
        using var response = await PostSendAsync(WebOrigin, token: "not-the-pairing-token-0000");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("agent_token_invalid", await CodeAsync(response));
    }

    [Fact]
    public async Task A_text_plain_body_is_refused_with_415()
    {
        await AgentStack.RequireAsync();
        using var request = new HttpRequestMessage(HttpMethod.Post, SendUri)
        {
            Content = new StringContent("{}", System.Text.Encoding.UTF8, "text/plain"),
        };
        request.Headers.TryAddWithoutValidation("Origin", WebOrigin);
        request.Headers.TryAddWithoutValidation(Token, "not-the-pairing-token-0000");

        using var response = await Http.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
        Assert.Equal("unsupported_media_type", await CodeAsync(response));
    }

    [Fact]
    public async Task Preflight_from_the_Web_origin_with_Private_Network_Access_is_answered_for_that_origin_only()
    {
        await AgentStack.RequireAsync();
        using var request = new HttpRequestMessage(HttpMethod.Options, SendUri);
        request.Headers.TryAddWithoutValidation("Origin", WebOrigin);
        request.Headers.TryAddWithoutValidation("Access-Control-Request-Method", "POST");
        request.Headers.TryAddWithoutValidation("Access-Control-Request-Headers", Token);
        request.Headers.TryAddWithoutValidation("Access-Control-Request-Private-Network", "true");

        using var response = await Http.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(WebOrigin, Header(response, "Access-Control-Allow-Origin"));
        Assert.Contains("POST", Header(response, "Access-Control-Allow-Methods"));
        Assert.Contains(Token, Header(response, "Access-Control-Allow-Headers"));
        Assert.Equal("true", Header(response, "Access-Control-Allow-Private-Network"));
        Assert.DoesNotContain("*", Header(response, "Access-Control-Allow-Origin"));
    }

    [Fact]
    public async Task Preflight_from_a_foreign_origin_gets_no_allow_header_not_even_for_private_network()
    {
        await AgentStack.RequireAsync();
        using var request = new HttpRequestMessage(HttpMethod.Options, SendUri);
        request.Headers.TryAddWithoutValidation("Origin", ForeignOrigin);
        request.Headers.TryAddWithoutValidation("Access-Control-Request-Method", "POST");
        request.Headers.TryAddWithoutValidation("Access-Control-Request-Private-Network", "true");

        using var response = await Http.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.False(response.Headers.Contains("Access-Control-Allow-Origin"));
        Assert.False(response.Headers.Contains("Access-Control-Allow-Private-Network"));
    }

    [Fact]
    public async Task A_second_listener_on_the_same_port_refuses_to_start()
    {
        await AgentStack.RequireAsync();
        Assert.True(File.Exists(AgentStack.ExecutablePath), $"The agent tool is missing at {AgentStack.ExecutablePath}, so the check cannot pass.");

        var (exitCode, error) = await AgentStack.StartSecondListenerAsync(TestContext.Current.CancellationToken);

        Assert.Equal(1, exitCode);
        Assert.Contains("in use", error);
    }

    /// <summary>A send with a body the agent would accept, apart from the token. The token check runs before the body is read.</summary>
    private static async Task<HttpResponseMessage> PostSendAsync(string origin, string? token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, SendUri)
        {
            Content = new StringContent("{\"request\":{\"method\":\"GET\",\"url\":\"http://127.0.0.1/x\"}}", System.Text.Encoding.UTF8, "application/json"),
        };
        request.Headers.TryAddWithoutValidation("Origin", origin);
        if (token is not null)
        {
            request.Headers.TryAddWithoutValidation(Token, token);
        }

        return await Http.SendAsync(request, TestContext.Current.CancellationToken);
    }

    /// <summary>The refusal's machine code (the problem document's "code"), or the empty string when the body is not that document.</summary>
    private static async Task<string> CodeAsync(HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        try
        {
            using var document = JsonDocument.Parse(text);
            return document.RootElement.TryGetProperty("code", out var code) ? code.GetString() ?? string.Empty : string.Empty;
        }
        catch (JsonException)
        {
            return string.Empty;
        }
    }

    private static string Header(HttpResponseMessage response, string name) =>
        response.Headers.TryGetValues(name, out var values) ? string.Join(", ", values) : string.Empty;
}
