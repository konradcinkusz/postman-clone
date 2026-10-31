using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using PostmanClone.ExecutionService.Api;
using PostmanClone.TestKit;
using Xunit;

namespace PostmanClone.ExecutionService.Tests.Support;

/// <summary>Posts to <c>/api/v1/execute</c> as one caller. A null token sends no credentials.</summary>
public sealed class SenderClient(HttpClient http, string? token)
{
    private static readonly JsonSerializerOptions ReadOptions = CreateReadOptions();

    private static readonly JsonSerializerOptions WriteOptions = new(JsonSerializerOptions.Web);

    public async Task<HttpResponseMessage> PostAsync(object body, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/execute")
        {
            Content = new StringContent(JsonSerializer.Serialize(body, WriteOptions), Encoding.UTF8, "application/json"),
        };
        if (token is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        return await http.SendAsync(request, cancellationToken);
    }

    /// <summary>Sends and reads a 200 answer as an <see cref="ExecutionResult"/>.</summary>
    public async Task<ExecutionResult> ExecuteAsync(object body, CancellationToken cancellationToken)
    {
        using var response = await PostAsync(body, cancellationToken);
        var text = await response.Content.ReadAsStringAsync(cancellationToken);
        Assert.True(response.IsSuccessStatusCode, $"Expected 200 from /execute, got {(int)response.StatusCode}: {text}");
        return JsonSerializer.Deserialize<ExecutionResult>(text, ReadOptions)!;
    }

    private static JsonSerializerOptions CreateReadOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerOptions.Web);
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        return options;
    }
}

/// <summary>Builds <c>ExecuteRequest</c> bodies in the CONTRACTS shape. Nulls are omitted by the caller's choice of arguments.</summary>
public static class Requests
{
    public static object Execute(
        string method,
        string url,
        object? headers = null,
        object? body = null,
        Guid? environmentId = null,
        int? timeoutSeconds = null,
        bool? followRedirects = null,
        object? auth = null) => new
        {
            request = new
            {
                method,
                url,
                @params = Array.Empty<object>(),
                headers = headers ?? Array.Empty<object>(),
                body = body ?? new { type = "none" },
                auth = auth ?? new { type = "none" },
            },
            environmentId,
            timeoutSeconds,
            followRedirects,
        };

    public static object Row(string key, string value) => new { key, value };

    public static object RawBody(string raw, string language = "text") => new { type = "raw", rawLanguage = language, raw, fields = Array.Empty<object>() };
}

/// <summary>A caller who may act in one workspace, with a ready client for the service under test.</summary>
public static class Sender
{
    public static async Task<SenderClient> ForAsync(ExecutionServiceFactory factory, Guid workspace, CancellationToken cancellationToken)
    {
        var http = await ServiceClient.ReadyAsync(factory, cancellationToken);
        var token = factory.Tokens.Issue(TestTokens.Membership(workspace, "Member"));
        return new SenderClient(http, token);
    }
}
