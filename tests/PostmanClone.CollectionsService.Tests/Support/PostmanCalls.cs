using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using PostmanClone.CollectionsService.Contracts;

namespace PostmanClone.CollectionsService.Tests.Support;

/// <summary>Calls the Postman routes (CONTRACTS §2) and builds the canonical view a round-trip test compares.</summary>
public static class PostmanCalls
{
    public static async Task<HttpResponseMessage> ImportJsonAsync(HttpClient client, string token, byte[] file, CancellationToken cancellationToken)
    {
        using var content = new ByteArrayContent(file);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        return await SendAsync(client, token, "/api/v1/import/postman", content, cancellationToken);
    }

    public static async Task<HttpResponseMessage> ImportMultipartAsync(HttpClient client, string token, byte[] file, CancellationToken cancellationToken)
    {
        using var form = new MultipartFormDataContent();
        form.Add(new ByteArrayContent(file), "file", "collection.json");
        return await SendAsync(client, token, "/api/v1/import/postman", form, cancellationToken);
    }

    public static Task<HttpResponseMessage> ExportAsync(HttpClient client, string token, Guid collection, CancellationToken cancellationToken) =>
        CollectionsCalls.SendAsync(client, HttpMethod.Get, $"/api/v1/collections/{collection}/export/postman", token, cancellationToken: cancellationToken);

    public static async Task<ImportResult> ImportedAsync(HttpClient client, string token, byte[] file, CancellationToken cancellationToken)
    {
        using var response = await ImportJsonAsync(client, token, file, cancellationToken);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ImportResult>(JsonSerializerOptions.Web, cancellationToken))!;
    }

    /// <summary>
    /// The collection as a sorted list of lines: each folder with its path and place, each request with its folder, name, place,
    /// description and whole definition. Two collections with the same lines hold the same structure, whatever their ids.
    /// </summary>
    public static async Task<IReadOnlyList<string>> CanonicalAsync(HttpClient client, string token, Guid collection, CancellationToken cancellationToken)
    {
        var tree = await RequestCalls.TreeAsync(client, token, collection, cancellationToken);
        var folders = tree.Folders.ToDictionary(folder => folder.Id);

        string PathOf(Guid? folderId)
        {
            var names = new List<string>();
            var current = folderId;
            while (current is { } id && folders.TryGetValue(id, out var folder))
            {
                names.Insert(0, folder.Name);
                current = folder.ParentFolderId;
            }

            return string.Join("/", names);
        }

        var lines = new List<string>();
        foreach (var folder in tree.Folders)
        {
            lines.Add($"folder|{PathOf(folder.Id)}|{folder.SortOrder}");
        }

        foreach (var summary in tree.Requests)
        {
            var request = await RequestCalls.ReadAsync(client, token, summary.Id, cancellationToken);
            lines.Add(string.Join("|",
                "request",
                PathOf(request.FolderId),
                request.Name,
                request.SortOrder,
                request.Description ?? string.Empty,
                JsonSerializer.Serialize(request.Request, JsonSerializerOptions.Web)));
        }

        lines.Sort(StringComparer.Ordinal);
        return lines;
    }

    private static async Task<HttpResponseMessage> SendAsync(HttpClient client, string token, string url, HttpContent content, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = content };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await client.SendAsync(request, cancellationToken);
    }
}
