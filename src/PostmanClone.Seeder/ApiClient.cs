using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Nodes;

namespace PostmanClone.Seeder;

/// <summary>A failed call to an estate API: carries the status and the response text (never a token or a request body).</summary>
internal sealed class SeedApiException(string call, HttpStatusCode status, string detail)
    : Exception($"{call} answered {(int)status}: {detail}")
{
    public HttpStatusCode Status { get; } = status;
}

/// <summary>
/// One HTTP client for the three APIs. A call is a method, a path under the base URL and an optional bearer token. Bodies are
/// JSON. A non-success answer throws <see cref="SeedApiException"/>; <see cref="TrySendAsync"/> returns the status instead.
/// </summary>
internal sealed class ApiClient(HttpClient http)
{
    public async Task<(HttpStatusCode Status, JsonNode? Body, string Text)> TrySendAsync(
        HttpMethod method, Uri baseUrl, string path, JsonNode? body = null, string? bearer = null, CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(method, new Uri(baseUrl, path));
        if (bearer is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
        }

        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        using var response = await http.SendAsync(request, cancellationToken);
        var text = await response.Content.ReadAsStringAsync(cancellationToken);
        JsonNode? parsed = null;
        if (text.Length > 0 && text[0] is '{' or '[')
        {
            try { parsed = JsonNode.Parse(text); } catch (System.Text.Json.JsonException) { }
        }

        return (response.StatusCode, parsed, text);
    }

    public async Task<JsonNode?> SendAsync(
        HttpMethod method, Uri baseUrl, string path, JsonNode? body = null, string? bearer = null, CancellationToken cancellationToken = default)
    {
        var (status, parsed, text) = await TrySendAsync(method, baseUrl, path, body, bearer, cancellationToken);
        if ((int)status is < 200 or >= 300)
        {
            // The response text of these APIs is a problem document or a short message; it holds no token.
            throw new SeedApiException($"{method} {baseUrl.Host}{path}", status, text.Length > 300 ? text[..300] : text);
        }

        return parsed;
    }
}
