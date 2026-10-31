using System.Net;
using PostmanClone.CollectionsService.Tests.Support;
using PostmanClone.TestKit;
using Xunit;

namespace PostmanClone.CollectionsService.Tests.Scoping;

/// <summary>
/// Tenant isolation for the two cURL routes that name a row (WP17, README §4): an import into a collection or folder of
/// another workspace, and an export of a request of another workspace, both answer <c>404 not_found</c>, never 403, and
/// change nothing. One <c>Scoping_&lt;METHOD&gt;_&lt;route&gt;</c> test exists per route.
/// </summary>
public sealed class CurlScopingTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    private const string ImportUrl = "/api/v1/import/curl";

    private const string Command = "curl https://api.example.invalid/planted";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [MemberData(nameof(Storage.Providers), MemberType = typeof(Storage))]
    public async Task Scoping_POST_import_curl(string storage)
    {
        using var tokens = new TestTokens();
        await using var factory = Storage.Start(tokens, storage, postgres);
        await SchemaWait.AppliedAsync(factory, Ct);
        using var client = factory.CreateClient();
        var ownerToken = CollectionsCalls.Token(tokens, Guid.NewGuid(), "Member");
        var foreignToken = CollectionsCalls.Token(tokens, Guid.NewGuid(), "Member");
        var collection = await CollectionsCalls.CreateCollectionAsync(client, ownerToken, "Owned", Ct);
        var folder = await CollectionsCalls.CreateFolderAsync(client, ownerToken, collection, "Owned folder", null, Ct);
        var foreignCollection = await CollectionsCalls.CreateCollectionAsync(client, foreignToken, "Foreign", Ct);
        var foreignFolder = await CollectionsCalls.CreateFolderAsync(client, foreignToken, foreignCollection, "Foreign folder", null, Ct);

        // The other workspace cannot import into the owner's collection.
        await ScopingAssertions.AssertNotFoundForOtherWorkspaceAsync(client, HttpMethod.Post, ImportUrl, foreignToken, Ct,
            System.Net.Http.Json.JsonContent.Create(new { command = Command, collectionId = collection, folderId = (Guid?)null }, options: System.Text.Json.JsonSerializerOptions.Web));

        // Nor into the owner's folder, the collection of which is the owner's.
        await ScopingAssertions.AssertNotFoundForOtherWorkspaceAsync(client, HttpMethod.Post, ImportUrl, foreignToken, Ct,
            System.Net.Http.Json.JsonContent.Create(new { command = Command, collectionId = collection, folderId = folder }, options: System.Text.Json.JsonSerializerOptions.Web));

        // The owner cannot import into the foreign collection either: the same 404, not a 403.
        using var intoForeignCollection = await CollectionsCalls.SendAsync(client, HttpMethod.Post, ImportUrl, ownerToken,
            new { command = Command, collectionId = foreignCollection, folderId = (Guid?)null }, Ct);
        Assert.Equal(HttpStatusCode.NotFound, intoForeignCollection.StatusCode);

        // And a folder of another workspace, named inside the owner's own collection, is absent for the owner.
        using var intoForeignFolder = await CollectionsCalls.SendAsync(client, HttpMethod.Post, ImportUrl, ownerToken,
            new { command = Command, collectionId = collection, folderId = foreignFolder }, Ct);
        Assert.Equal(HttpStatusCode.NotFound, intoForeignFolder.StatusCode);

        // Nothing was written by any refused import.
        var ownTree = await RequestCalls.TreeAsync(client, ownerToken, collection, Ct);
        Assert.Empty(ownTree.Requests);
        var foreignTree = await RequestCalls.TreeAsync(client, foreignToken, foreignCollection, Ct);
        Assert.Empty(foreignTree.Requests);
    }

    [Theory]
    [MemberData(nameof(Storage.Providers), MemberType = typeof(Storage))]
    public async Task Scoping_GET_requests_id_export_curl(string storage)
    {
        using var tokens = new TestTokens();
        await using var factory = Storage.Start(tokens, storage, postgres);
        await SchemaWait.AppliedAsync(factory, Ct);
        using var client = factory.CreateClient();
        var ownerToken = CollectionsCalls.Token(tokens, Guid.NewGuid(), "Member");
        var foreignToken = CollectionsCalls.Token(tokens, Guid.NewGuid(), "Member");
        var collection = await CollectionsCalls.CreateCollectionAsync(client, ownerToken, "Owned", Ct);
        var request = await RequestCalls.CreateRequestAsync(client, ownerToken, collection, "Get items", null, RequestCalls.Sample(), Ct);

        await ScopingAssertions.AssertNotFoundForOtherWorkspaceAsync(client, HttpMethod.Get,
            $"/api/v1/requests/{request}/export/curl", foreignToken, Ct);

        using var own = await CollectionsCalls.SendAsync(client, HttpMethod.Get, $"/api/v1/requests/{request}/export/curl", ownerToken, cancellationToken: Ct);
        Assert.Equal(HttpStatusCode.OK, own.StatusCode);
        Assert.Contains("https://example.invalid/items", await own.Content.ReadAsStringAsync(Ct));
    }
}
