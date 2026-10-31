using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using PostmanClone.TestKit;
using Xunit;
using PostmanClone.CollectionsService.Tests.Support;

namespace PostmanClone.CollectionsService.Tests.Documentation;

/// <summary>The route list is served in Development only, and only to a signed-in caller (WP08 acceptance, CONTRACTS §0).</summary>
public sealed class OpenApiTests
{
    private const string DocumentUrl = "/openapi/v1.json";

    [Fact]
    public async Task Development_serves_a_document_listing_every_collections_route_to_a_signed_in_caller()
    {
        using var tokens = new TestTokens();
        await using var factory = new CollectionsFactory(tokens);
        await SchemaWait.AppliedAsync(factory, TestContext.Current.CancellationToken);
        using var client = factory.CreateClient();

        using var request = new HttpRequestMessage(HttpMethod.Get, DocumentUrl);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", CollectionsCalls.Token(tokens, Guid.NewGuid(), "Member"));
        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        var paths = document.RootElement.GetProperty("paths").EnumerateObject().Select(path => path.Name).ToHashSet();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        // Every route named here must be in the document. The document may list more (WP16 adds the Postman routes).
        var expected = new[]
        {
            "/api/v1/collections",
            "/api/v1/collections/{id}",
            "/api/v1/collections/{id}/export/postman",
            "/api/v1/collections/{id}/folders",
            "/api/v1/collections/{id}/requests",
            "/api/v1/folders/{id}",
            "/api/v1/folders/{id}/move",
            "/api/v1/import/postman",
            "/api/v1/requests/{id}",
            "/api/v1/requests/{id}/duplicate",
            "/api/v1/requests/{id}/move",
        };
        foreach (var route in expected)
        {
            Assert.Contains(route, paths);
        }
    }

    [Fact]
    public async Task Development_refuses_the_document_without_a_token()
    {
        using var tokens = new TestTokens();
        await using var factory = new CollectionsFactory(tokens);
        await SchemaWait.AppliedAsync(factory, TestContext.Current.CancellationToken);
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(DocumentUrl, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Production_does_not_serve_the_document()
    {
        using var tokens = new TestTokens();
        await using var factory = new CollectionsFactory(tokens, environment: "Production");
        await SchemaWait.AppliedAsync(factory, TestContext.Current.CancellationToken);
        using var client = factory.CreateClient();

        using var request = new HttpRequestMessage(HttpMethod.Get, DocumentUrl);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", CollectionsCalls.Token(tokens, Guid.NewGuid(), "Member"));
        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
