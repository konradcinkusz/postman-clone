using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using PostmanClone.TestKit;

namespace PostmanClone.CollectionsService.Tests.Support;

/// <summary>Calls the collections API with a bearer token and reads the answers. The bodies are sent as the web defaults write them.</summary>
public static class CollectionsCalls
{
    /// <summary>A token that names one membership. Each call issues a new user, so tokens from one call are the same user.</summary>
    public static string Token(TestTokens tokens, Guid workspace, string role) =>
        tokens.Issue(TestTokens.Membership(workspace, role));

    /// <summary>The user the token's <c>sub</c> names: the creator a new collection records.</summary>
    public static Guid SubjectOf(string token) =>
        Guid.Parse(new JwtSecurityTokenHandler().ReadJwtToken(token).Subject);

    public static Task<HttpResponseMessage> SendAsync(
        HttpClient client,
        HttpMethod method,
        string url,
        string token,
        object? body = null,
        CancellationToken cancellationToken = default)
    {
        var request = new HttpRequestMessage(method, url)
        {
            Content = body is null ? null : JsonContent.Create(body, options: JsonSerializerOptions.Web),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client.SendAsync(request, cancellationToken);
    }

    public static async Task<Guid> CreateCollectionAsync(HttpClient client, string token, string name, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(client, HttpMethod.Post, "/api/v1/collections", token, new { name }, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await IdAsync(response, cancellationToken);
    }

    public static async Task<Guid> CreateFolderAsync(
        HttpClient client,
        string token,
        Guid collectionId,
        string name,
        Guid? parentFolderId,
        CancellationToken cancellationToken)
    {
        using var response = await SendAsync(client, HttpMethod.Post, $"/api/v1/collections/{collectionId}/folders", token,
            new { name, parentFolderId }, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await IdAsync(response, cancellationToken);
    }

    /// <summary>The <c>id</c> of a success body, as a <see cref="Guid"/>.</summary>
    public static async Task<Guid> IdAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        return document.RootElement.GetProperty("id").GetGuid();
    }

    /// <summary>The body as a JSON document the caller reads and disposes.</summary>
    public static async Task<JsonDocument> JsonAsync(HttpResponseMessage response, CancellationToken cancellationToken) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
}
