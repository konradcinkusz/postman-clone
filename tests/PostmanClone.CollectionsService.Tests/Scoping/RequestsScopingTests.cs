using System.Net;
using System.Net.Http.Json;
using PostmanClone.CollectionsService.Contracts;
using PostmanClone.CollectionsService.Tests.Support;
using PostmanClone.TestKit;
using Xunit;

namespace PostmanClone.CollectionsService.Tests.Scoping;

/// <summary>
/// Tenant isolation for every saved-request id endpoint (CONTRACTS §0, README §4): a row of another workspace answers
/// <c>404 not_found</c>, never 403, and is left unchanged. Moves and duplicates are checked against a foreign collection
/// and a foreign folder too, because naming one of those is the same leak by another route.
/// One <c>Scoping_&lt;METHOD&gt;_&lt;route&gt;</c> test exists per id endpoint.
/// </summary>
public sealed class RequestsScopingTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [MemberData(nameof(Storage.Providers), MemberType = typeof(Storage))]
    public async Task Scoping_POST_collections_id_requests(string storage)
    {
        using var tokens = new TestTokens();
        await using var factory = Storage.Start(tokens, storage, postgres);
        await SchemaWait.AppliedAsync(factory, Ct);
        using var client = factory.CreateClient();
        var owner = await OwnedRowsAsync(tokens, client);

        await ScopingAssertions.AssertNotFoundForOtherWorkspaceAsync(client, HttpMethod.Post,
            $"/api/v1/collections/{owner.Collection}/requests", CollectionsCalls.Token(tokens, Guid.NewGuid(), "Owner"), Ct,
            JsonContent.Create(new { name = "Planted", request = RequestCalls.Sample() }, options: System.Text.Json.JsonSerializerOptions.Web));

        // A folder of another workspace cannot hold a request of this one: the folder is absent for the owner.
        var foreignCollection = await CollectionsCalls.CreateCollectionAsync(client, owner.ForeignToken, "Foreign", Ct);
        var foreignFolder = await CollectionsCalls.CreateFolderAsync(client, owner.ForeignToken, foreignCollection, "Foreign folder", null, Ct);
        using var planted = await CollectionsCalls.SendAsync(client, HttpMethod.Post, $"/api/v1/collections/{owner.Collection}/requests", owner.Token,
            new { name = "Planted", folderId = foreignFolder, request = RequestCalls.Sample() }, Ct);
        Assert.Equal(HttpStatusCode.NotFound, planted.StatusCode);

        var foreignTree = await RequestCalls.TreeAsync(client, owner.ForeignToken, foreignCollection, Ct);
        Assert.DoesNotContain("Planted", foreignTree.Requests.Select(row => row.Name));
        var ownTree = await RequestCalls.TreeAsync(client, owner.Token, owner.Collection, Ct);
        Assert.Equal(new[] { "Get items" }, RequestCalls.Slot(ownTree, null).Select(row => row.Name));
    }

    [Theory]
    [MemberData(nameof(Storage.Providers), MemberType = typeof(Storage))]
    public async Task Scoping_GET_requests_id(string storage)
    {
        using var tokens = new TestTokens();
        await using var factory = Storage.Start(tokens, storage, postgres);
        await SchemaWait.AppliedAsync(factory, Ct);
        using var client = factory.CreateClient();
        var owner = await OwnedRowsAsync(tokens, client);

        await ScopingAssertions.AssertNotFoundForOtherWorkspaceAsync(client, HttpMethod.Get,
            $"/api/v1/requests/{owner.Request}", CollectionsCalls.Token(tokens, Guid.NewGuid(), "Owner"), Ct);

        var read = await RequestCalls.ReadAsync(client, owner.Token, owner.Request, Ct);
        Assert.Equal("Get items", read.Name);
    }

    [Theory]
    [MemberData(nameof(Storage.Providers), MemberType = typeof(Storage))]
    public async Task Scoping_PUT_requests_id(string storage)
    {
        using var tokens = new TestTokens();
        await using var factory = Storage.Start(tokens, storage, postgres);
        await SchemaWait.AppliedAsync(factory, Ct);
        using var client = factory.CreateClient();
        var owner = await OwnedRowsAsync(tokens, client);

        await ScopingAssertions.AssertNotFoundForOtherWorkspaceAsync(client, HttpMethod.Put,
            $"/api/v1/requests/{owner.Request}", CollectionsCalls.Token(tokens, Guid.NewGuid(), "Owner"), Ct,
            JsonContent.Create(new { name = "Hijacked", request = RequestCalls.Sample("https://example.invalid/hijacked") }, options: System.Text.Json.JsonSerializerOptions.Web));

        var read = await RequestCalls.ReadAsync(client, owner.Token, owner.Request, Ct);
        Assert.Equal("Get items", read.Name);
        Assert.Equal(1, read.Version);
    }

    [Theory]
    [MemberData(nameof(Storage.Providers), MemberType = typeof(Storage))]
    public async Task Scoping_POST_requests_id_duplicate(string storage)
    {
        using var tokens = new TestTokens();
        await using var factory = Storage.Start(tokens, storage, postgres);
        await SchemaWait.AppliedAsync(factory, Ct);
        using var client = factory.CreateClient();
        var owner = await OwnedRowsAsync(tokens, client);

        await ScopingAssertions.AssertNotFoundForOtherWorkspaceAsync(client, HttpMethod.Post,
            $"/api/v1/requests/{owner.Request}/duplicate", CollectionsCalls.Token(tokens, Guid.NewGuid(), "Owner"), Ct);

        var tree = await RequestCalls.TreeAsync(client, owner.Token, owner.Collection, Ct);
        Assert.Equal(new[] { "Get items" }, tree.Requests.Select(row => row.Name));
    }

    [Theory]
    [MemberData(nameof(Storage.Providers), MemberType = typeof(Storage))]
    public async Task Scoping_POST_requests_id_move(string storage)
    {
        using var tokens = new TestTokens();
        await using var factory = Storage.Start(tokens, storage, postgres);
        await SchemaWait.AppliedAsync(factory, Ct);
        using var client = factory.CreateClient();
        var owner = await OwnedRowsAsync(tokens, client);

        await ScopingAssertions.AssertNotFoundForOtherWorkspaceAsync(client, HttpMethod.Post,
            $"/api/v1/requests/{owner.Request}/move", CollectionsCalls.Token(tokens, Guid.NewGuid(), "Owner"), Ct,
            JsonContent.Create(new { collectionId = (Guid?)null, folderId = (Guid?)null, sortOrder = (int?)null }, options: System.Text.Json.JsonSerializerOptions.Web));

        // Moving the owner's request into a collection of another workspace is refused as an absent collection.
        var foreignCollection = await CollectionsCalls.CreateCollectionAsync(client, owner.ForeignToken, "Foreign", Ct);
        using var intoForeignCollection = await CollectionsCalls.SendAsync(client, HttpMethod.Post, $"/api/v1/requests/{owner.Request}/move", owner.Token,
            new { collectionId = foreignCollection, folderId = (Guid?)null, sortOrder = (int?)null }, Ct);
        Assert.Equal(HttpStatusCode.NotFound, intoForeignCollection.StatusCode);

        // And into a folder of another workspace inside the owner's own collection.
        var foreignFolder = await CollectionsCalls.CreateFolderAsync(client, owner.ForeignToken, foreignCollection, "Foreign folder", null, Ct);
        using var intoForeignFolder = await CollectionsCalls.SendAsync(client, HttpMethod.Post, $"/api/v1/requests/{owner.Request}/move", owner.Token,
            new { collectionId = (Guid?)null, folderId = foreignFolder, sortOrder = (int?)null }, Ct);
        Assert.Equal(HttpStatusCode.NotFound, intoForeignFolder.StatusCode);

        var tree = await RequestCalls.TreeAsync(client, owner.Token, owner.Collection, Ct);
        Assert.Equal(new[] { ("Get items", 0) }, RequestCalls.Slot(tree, null));
    }

    [Theory]
    [MemberData(nameof(Storage.Providers), MemberType = typeof(Storage))]
    public async Task Scoping_DELETE_requests_id(string storage)
    {
        using var tokens = new TestTokens();
        await using var factory = Storage.Start(tokens, storage, postgres);
        await SchemaWait.AppliedAsync(factory, Ct);
        using var client = factory.CreateClient();
        var owner = await OwnedRowsAsync(tokens, client);

        await ScopingAssertions.AssertNotFoundForOtherWorkspaceAsync(client, HttpMethod.Delete,
            $"/api/v1/requests/{owner.Request}", CollectionsCalls.Token(tokens, Guid.NewGuid(), "Owner"), Ct);

        var read = await RequestCalls.ReadAsync(client, owner.Token, owner.Request, Ct);
        Assert.Equal("Get items", read.Name);
    }

    /// <summary>One collection, one folder and one request in a fresh workspace, and a token of a second workspace.</summary>
    private static async Task<OwnedRows> OwnedRowsAsync(TestTokens tokens, HttpClient client)
    {
        var token = CollectionsCalls.Token(tokens, Guid.NewGuid(), "Member");
        var collection = await CollectionsCalls.CreateCollectionAsync(client, token, "Payments", Ct);
        var folder = await CollectionsCalls.CreateFolderAsync(client, token, collection, "Auth", null, Ct);
        var request = await RequestCalls.CreateRequestAsync(client, token, collection, "Get items", null, RequestCalls.Sample(), Ct);
        return new OwnedRows(token, CollectionsCalls.Token(tokens, Guid.NewGuid(), "Member"), collection, folder, request);
    }

    private sealed record OwnedRows(string Token, string ForeignToken, Guid Collection, Guid Folder, Guid Request);
}
