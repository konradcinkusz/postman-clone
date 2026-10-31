using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using PostmanClone.CollectionsService.Contracts;
using PostmanClone.CollectionsService.Domain;

namespace PostmanClone.CollectionsService.Tests.Support;

/// <summary>Calls the saved-request routes and builds the definitions the tests send.</summary>
public static class RequestCalls
{
    public const string BaseUrl = "https://example.invalid/items";

    /// <summary>A small, valid GET request with one parameter and one header.</summary>
    public static RequestDefinition Sample(string url = BaseUrl) => new(
        "GET",
        url,
        [new KeyValue("page", "1")],
        [new KeyValue("Accept", "application/json")],
        new RequestBody("none", null, null, []),
        new RequestAuth("none", null, null, null, null, null, null));

    /// <summary>The canonical JSON of a definition, for comparing one that went through storage with the one sent.</summary>
    public static string Canonical(RequestDefinition definition) => JsonSerializer.Serialize(definition, JsonSerializerOptions.Web);

    public static Task<HttpResponseMessage> SendWithIfMatchAsync(
        HttpClient client,
        HttpMethod method,
        string url,
        string token,
        object? body,
        string? ifMatch,
        CancellationToken cancellationToken)
    {
        var request = new HttpRequestMessage(method, url)
        {
            Content = body is null ? null : JsonContent.Create(body, options: JsonSerializerOptions.Web),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (ifMatch is not null)
        {
            request.Headers.TryAddWithoutValidation("If-Match", ifMatch);
        }

        return client.SendAsync(request, cancellationToken);
    }

    public static async Task<Guid> CreateRequestAsync(
        HttpClient client,
        string token,
        Guid collection,
        string name,
        Guid? folder,
        RequestDefinition request,
        CancellationToken cancellationToken)
    {
        using var response = await CollectionsCalls.SendAsync(client, HttpMethod.Post, $"/api/v1/collections/{collection}/requests", token,
            new { name, folderId = folder, request }, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await CollectionsCalls.IdAsync(response, cancellationToken);
    }

    public static async Task<SavedRequestDto> ReadAsync(HttpClient client, string token, Guid id, CancellationToken cancellationToken)
    {
        using var response = await CollectionsCalls.SendAsync(client, HttpMethod.Get, $"/api/v1/requests/{id}", token, cancellationToken: cancellationToken);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<SavedRequestDto>(cancellationToken))!;
    }

    public static async Task<CollectionTree> TreeAsync(HttpClient client, string token, Guid collection, CancellationToken cancellationToken)
    {
        using var response = await CollectionsCalls.SendAsync(client, HttpMethod.Get, $"/api/v1/collections/{collection}", token, cancellationToken: cancellationToken);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<CollectionTree>(cancellationToken))!;
    }

    /// <summary>The request names of one folder (null: the top of the collection), in sort order, with their sort orders.</summary>
    public static IReadOnlyList<(string Name, int SortOrder)> Slot(CollectionTree tree, Guid? folder) =>
        tree.Requests
            .Where(request => request.FolderId == folder)
            .OrderBy(request => request.SortOrder)
            .Select(request => (request.Name, request.SortOrder))
            .ToList();
}

/// <summary>
/// Builds a valid definition whose JSON is exactly a given number of bytes, so the 1.5 MB limit can be tested at its edge.
/// Raw text takes the bulk, header rows the rest, and the URL the last few bytes. Every value is plain ASCII, so the size
/// is the length of the text.
/// </summary>
public static class RequestSizing
{
    private const string BaseUrl = RequestCalls.BaseUrl;

    public static RequestDefinition OfSerializedSize(int bytes)
    {
        var raw = new string('a', Math.Min(RequestDefinitionLimits.MaxRawLength, Math.Max(0, bytes - Size(Build(string.Empty, [], BaseUrl)))));
        var headers = new List<KeyValue>();
        while (true)
        {
            var current = Size(Build(raw, headers, BaseUrl));
            var remaining = bytes - current;

            // What one more row costs with an empty value: its braces, key, comma and the fixed fields.
            var rowOverhead = Size(Build(raw, [.. headers, Row(string.Empty)], BaseUrl)) - current;
            if (remaining < rowOverhead)
            {
                break;
            }

            headers.Add(Row(new string('a', Math.Min(RequestDefinitionLimits.MaxValueLength, remaining - rowOverhead))));
        }

        // The rest is smaller than one row, so the URL takes it, one character per byte.
        var rest = bytes - Size(Build(raw, headers, BaseUrl));
        return Build(raw, headers, BaseUrl + new string('u', rest));
    }

    private static KeyValue Row(string value) => new("X-Padding", value);

    private static RequestDefinition Build(string raw, IReadOnlyList<KeyValue> headers, string url) => new(
        "POST",
        url,
        [],
        headers,
        new RequestBody("raw", "text", raw, []),
        new RequestAuth("none", null, null, null, null, null, null));

    private static int Size(RequestDefinition definition) =>
        System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(definition, JsonSerializerOptions.Web).Length;
}
