using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using PostmanClone.ExecutionService.Api;
using PostmanClone.Sending.Contracts;
using PostmanClone.TestKit;
using Xunit;

namespace PostmanClone.ExecutionService.Tests.Support;

/// <summary>
/// One user acting in one workspace against the history endpoints and the sender, with a ready client. The token's <c>sub</c>
/// is the user id, so "same workspace, other user" and "same user, other workspace" are two callers built from the same ids.
/// </summary>
public sealed class HistoryCaller
{
    private static readonly JsonSerializerOptions WriteOptions = new(JsonSerializerOptions.Web);

    private static readonly JsonSerializerOptions ReadOptions = CreateReadOptions();

    private readonly HttpClient _http;
    private readonly string? _token;

    private HistoryCaller(HttpClient http, string? token)
    {
        _http = http;
        _token = token;
    }

    /// <summary>A caller of <paramref name="workspace"/> whose subject is <paramref name="user"/>.</summary>
    public static Task<HistoryCaller> ForAsync(ExecutionServiceFactory factory, Guid workspace, Guid user, CancellationToken cancellationToken) =>
        ForAsync(factory, workspace, user.ToString(), cancellationToken);

    /// <summary>A caller whose subject is any string, for the case where the subject is not a user id.</summary>
    public static async Task<HistoryCaller> ForAsync(ExecutionServiceFactory factory, Guid workspace, string subject, CancellationToken cancellationToken)
    {
        var http = await ServiceClient.ReadyAsync(factory, cancellationToken);
        var token = factory.Tokens.Issue([new Claim("sub", subject), .. TestTokens.Membership(workspace, "Member")]);
        return new HistoryCaller(http, token);
    }

    /// <summary>A request with no credentials, for the 401 case.</summary>
    public static async Task<HttpResponseMessage> AnonymousListAsync(ExecutionServiceFactory factory, CancellationToken cancellationToken)
    {
        var http = await ServiceClient.ReadyAsync(factory, cancellationToken);
        return await http.GetAsync("/api/v1/history", cancellationToken);
    }

    public Task<HttpResponseMessage> ListAsync(string query, CancellationToken cancellationToken) =>
        SendAsync(HttpMethod.Get, $"/api/v1/history{query}", null, cancellationToken);

    public Task<HttpResponseMessage> GetAsync(Guid id, CancellationToken cancellationToken) =>
        SendAsync(HttpMethod.Get, $"/api/v1/history/{id}", null, cancellationToken);

    public Task<HttpResponseMessage> DeleteAsync(Guid id, CancellationToken cancellationToken) =>
        SendAsync(HttpMethod.Delete, $"/api/v1/history/{id}", null, cancellationToken);

    public Task<HttpResponseMessage> ClearAsync(CancellationToken cancellationToken) =>
        SendAsync(HttpMethod.Delete, "/api/v1/history", null, cancellationToken);

    public Task<HttpResponseMessage> ExecuteAsync(object body, CancellationToken cancellationToken) =>
        SendAsync(HttpMethod.Post, "/api/v1/execute", body, cancellationToken);

    public async Task<Page<HistorySummary>> ListPageAsync(string query, CancellationToken cancellationToken) =>
        await ReadAsync<Page<HistorySummary>>(await ListAsync(query, cancellationToken));

    public async Task<HistoryEntry> EntryAsync(Guid id, CancellationToken cancellationToken) =>
        await ReadAsync<HistoryEntry>(await GetAsync(id, cancellationToken));

    /// <summary>Sends, and reads the 200 answer as the result. Fails the test on any other status.</summary>
    public async Task<ExecutionResult> ExecuteResultAsync(object body, CancellationToken cancellationToken) =>
        await ReadAsync<ExecutionResult>(await ExecuteAsync(body, cancellationToken));

    /// <summary>Reads a 200 answer. A non-success status fails the test with its body, so a refusal is visible.</summary>
    public static async Task<T> ReadAsync<T>(HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"Expected success, got {(int)response.StatusCode}: {text}");
        return JsonSerializer.Deserialize<T>(text, ReadOptions)!;
    }

    /// <summary>Asserts a 404 with the stable code <c>not_found</c>. The answer is never 403 for a row that exists elsewhere (CONTRACTS §0).</summary>
    public static async Task AssertNotFoundAsync(HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Contains("\"code\":\"not_found\"", text);
    }

    /// <summary>The request object of a body with no headers, params, body or auth beyond the defaults.</summary>
    public static object Get(string url, object? headers = null, object? auth = null, object? body = null) => new
    {
        method = "GET",
        url,
        @params = Array.Empty<object>(),
        headers = headers ?? Array.Empty<object>(),
        body = body ?? new { type = "none" },
        auth = auth ?? new { type = "none" },
    };

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, object? body, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, path);
        if (_token is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _token);
        }

        if (body is not null)
        {
            request.Content = new StringContent(JsonSerializer.Serialize(body, WriteOptions), Encoding.UTF8, "application/json");
        }

        return await _http.SendAsync(request, cancellationToken);
    }

    private static JsonSerializerOptions CreateReadOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerOptions.Web);
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        return options;
    }
}
