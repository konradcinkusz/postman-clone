using System.Net;
using System.Runtime.InteropServices;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PostmanClone.Agent.Remote;

/// <summary>What the server answered, reduced to the status and the contract code. Bodies are read only for the success shapes.</summary>
public sealed record ServerReply<T>(int Status, T? Body, string? Code)
{
    public bool Ok => Status is >= 200 and < 300 && Body is not null;

    /// <summary>The server could not be reached or answered something the agent does not understand.</summary>
    public bool Unavailable => Status == 0 || Status >= 500;
}

/// <summary>The agent's pairing as the server returns it (CONTRACTS A2.3). The token is shown once.</summary>
public sealed record AgentPairingReply(Guid AgentId, string AgentToken, DateTimeOffset ExpiresAt, Guid WorkspaceId, IReadOnlyList<string> AllowedWebOrigins);

/// <summary>An environment's enabled variables, secrets in plaintext (CONTRACTS A2.4). Held in memory for one send only.</summary>
public sealed record ResolvedEnvironmentReply(Guid Id, IReadOnlyList<ResolvedVariableReply> Variables);

public sealed record ResolvedVariableReply(string Key, string Value, bool IsSecret);

/// <summary>The agent's own identity as the server knows it (CONTRACTS A2.4).</summary>
public sealed record AgentIdentityReply(Guid AgentId, Guid WorkspaceId, DateTimeOffset ExpiresAt);

/// <summary>The calls the agent makes to EnvironmentsService (CONTRACTS A2.3, A2.4). The interface lets the tests replace the server.</summary>
public interface IAgentServer
{
    Task<ServerReply<AgentPairingReply>> PairAsync(string baseUrl, string code, string agentName, string platform, string version, CancellationToken cancellationToken);

    Task<ServerReply<ResolvedEnvironmentReply>> ResolveAsync(string baseUrl, string agentToken, Guid environmentId, CancellationToken cancellationToken);

    Task<ServerReply<AgentIdentityReply>> MeAsync(string baseUrl, string agentToken, CancellationToken cancellationToken);
}

/// <summary>
/// The HTTP client for the agent's server calls. No ambient proxy, no cookies, no automatic redirects, and explicit timeouts:
/// a server answer that is a redirect is a failure, not something the agent follows with its token.
/// </summary>
public sealed class AgentServerClient : IAgentServer, IDisposable
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _http;

    public AgentServerClient(HttpMessageHandler? handler = null)
    {
        var inner = handler ?? new SocketsHttpHandler
        {
            UseProxy = false,
            Proxy = null,
            UseCookies = false,
            AllowAutoRedirect = false,
            ConnectTimeout = TimeSpan.FromSeconds(10),
        };
        _http = new HttpClient(inner, disposeHandler: true) { Timeout = Timeout };
        _http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("postmanclone-agent", AgentInfo.Version));
    }

    public async Task<ServerReply<AgentPairingReply>> PairAsync(string baseUrl, string code, string agentName, string platform, string version, CancellationToken cancellationToken)
    {
        var body = new { code, agentName, platform, version };
        return await SendAsync<AgentPairingReply>(HttpMethod.Post, Url(baseUrl, "/api/v1/agent/pair"), null, body, cancellationToken);
    }

    public Task<ServerReply<ResolvedEnvironmentReply>> ResolveAsync(string baseUrl, string agentToken, Guid environmentId, CancellationToken cancellationToken) =>
        SendAsync<ResolvedEnvironmentReply>(HttpMethod.Post, Url(baseUrl, $"/api/v1/agent/environments/{environmentId}/resolve"), agentToken, null, cancellationToken);

    public Task<ServerReply<AgentIdentityReply>> MeAsync(string baseUrl, string agentToken, CancellationToken cancellationToken) =>
        SendAsync<AgentIdentityReply>(HttpMethod.Get, Url(baseUrl, "/api/v1/agent/me"), agentToken, null, cancellationToken);

    public void Dispose() => _http.Dispose();

    private static Uri Url(string baseUrl, string path) => new(new Uri(baseUrl.TrimEnd('/') + "/", UriKind.Absolute), path.TrimStart('/'));

    private async Task<ServerReply<T>> SendAsync<T>(HttpMethod method, Uri url, string? bearer, object? body, CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(method, url);
            if (bearer is not null)
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
            }

            if (body is not null)
            {
                request.Content = JsonContent.Create(body, options: Json);
            }

            using var response = await _http.SendAsync(request, cancellationToken);
            var status = (int)response.StatusCode;
            if (response.StatusCode is HttpStatusCode.Redirect or HttpStatusCode.Moved or HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect)
            {
                return new ServerReply<T>(0, default, "upstream_unavailable");
            }

            var text = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return new ServerReply<T>(status, default, ReadCode(text));
            }

            var parsed = JsonSerializer.Deserialize<T>(text, Json);
            return new ServerReply<T>(status, parsed, parsed is null ? "upstream_unavailable" : null);
        }
        catch (Exception failure) when (failure is HttpRequestException or TaskCanceledException or IOException or JsonException)
        {
            // The message of the failure is not logged: it can name the URL, and the URL names the environment.
            return new ServerReply<T>(0, default, "upstream_unavailable");
        }
    }

    /// <summary>The <c>code</c> extension of a problem response (CONTRACTS §0), or null.</summary>
    private static string? ReadCode(string text)
    {
        try
        {
            using var document = JsonDocument.Parse(text);
            return document.RootElement.TryGetProperty("code", out var code) && code.ValueKind == JsonValueKind.String
                ? code.GetString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

/// <summary>What the agent says about itself: its name and version, as the server and the local status see them.</summary>
public static class AgentInfo
{
    public const string Name = "postmanclone-agent";

    public const int Protocol = 1;

    public static string Version { get; } =
        typeof(AgentInfo).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";

    /// <summary>The platform label sent at pairing, such as <c>linux-x64</c>.</summary>
    public static string Platform { get; } =
        $"{(OperatingSystem.IsWindows() ? "windows" : OperatingSystem.IsMacOS() ? "macos" : "linux")}-{RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant()}";
}
