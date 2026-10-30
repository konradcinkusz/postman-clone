using System.Net;
using System.Net.Http.Json;
using PostmanClone.CollectionsService.Contracts;
using PostmanClone.CollectionsService.Tests.Support;
using PostmanClone.TestKit;
using Xunit;

namespace PostmanClone.CollectionsService.Tests.Scoping;

/// <summary>
/// Tenant isolation for every id endpoint of the collections API (CONTRACTS §0, README §4). Each test builds a collection
/// and a folder in one workspace, then calls the endpoint with a token of another workspace: the answer is 404 not_found,
/// never 403, and the row is unchanged. The owner's own call, with the same ids, still works.
/// One <c>Scoping_&lt;METHOD&gt;_&lt;route&gt;</c> test exists per id endpoint so the matrix test of WP25 can find it.
/// </summary>
public sealed class CollectionsScopingTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    [Theory]
    [MemberData(nameof(Storage.Providers), MemberType = typeof(Storage))]
    public async Task Scoping_GET_collections_id(string storage)
    {
        using var tokens = new TestTokens();
        await using var factory = Storage.Start(tokens, storage, postgres);
        await SchemaWait.AppliedAsync(factory, TestContext.Current.CancellationToken);
        using var client = factory.CreateClient();
        var owner = await OwnedRowsAsync(tokens, client);

        await ScopingAssertions.AssertNotFoundForOtherWorkspaceAsync(client, HttpMethod.Get,
            $"/api/v1/collections/{owner.Collection}", CollectionsCalls.Token(tokens, Guid.NewGuid(), "Owner"), TestContext.Current.CancellationToken);
    }

    [Theory]
    [MemberData(nameof(Storage.Providers), MemberType = typeof(Storage))]
    public async Task Scoping_PUT_collections_id(string storage)
    {
        using var tokens = new TestTokens();
        await using var factory = Storage.Start(tokens, storage, postgres);
        await SchemaWait.AppliedAsync(factory, TestContext.Current.CancellationToken);
        using var client = factory.CreateClient();
        var owner = await OwnedRowsAsync(tokens, client);

        await ScopingAssertions.AssertNotFoundForOtherWorkspaceAsync(client, HttpMethod.Put,
            $"/api/v1/collections/{owner.Collection}", CollectionsCalls.Token(tokens, Guid.NewGuid(), "Owner"), TestContext.Current.CancellationToken,
            JsonContent.Create(new { name = "Hijacked" }, options: System.Text.Json.JsonSerializerOptions.Web));

        using var tree = await CollectionsCalls.SendAsync(client, HttpMethod.Get, $"/api/v1/collections/{owner.Collection}", owner.Token, cancellationToken: TestContext.Current.CancellationToken);
        var read = await tree.Content.ReadFromJsonAsync<CollectionTree>(TestContext.Current.CancellationToken);
        Assert.Equal("Payments", read!.Collection.Name);
    }

    [Theory]
    [MemberData(nameof(Storage.Providers), MemberType = typeof(Storage))]
    public async Task Scoping_DELETE_collections_id(string storage)
    {
        using var tokens = new TestTokens();
        await using var factory = Storage.Start(tokens, storage, postgres);
        await SchemaWait.AppliedAsync(factory, TestContext.Current.CancellationToken);
        using var client = factory.CreateClient();
        var owner = await OwnedRowsAsync(tokens, client);

        // An Owner of the other workspace is refused too: the 404 comes before the role check, so the row's existence is not revealed.
        await ScopingAssertions.AssertNotFoundForOtherWorkspaceAsync(client, HttpMethod.Delete,
            $"/api/v1/collections/{owner.Collection}", CollectionsCalls.Token(tokens, Guid.NewGuid(), "Owner"), TestContext.Current.CancellationToken);

        using var stillThere = await CollectionsCalls.SendAsync(client, HttpMethod.Get, $"/api/v1/collections/{owner.Collection}", owner.Token, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, stillThere.StatusCode);
    }

    [Theory]
    [MemberData(nameof(Storage.Providers), MemberType = typeof(Storage))]
    public async Task Scoping_POST_collections_id_folders(string storage)
    {
        using var tokens = new TestTokens();
        await using var factory = Storage.Start(tokens, storage, postgres);
        await SchemaWait.AppliedAsync(factory, TestContext.Current.CancellationToken);
        using var client = factory.CreateClient();
        var owner = await OwnedRowsAsync(tokens, client);

        await ScopingAssertions.AssertNotFoundForOtherWorkspaceAsync(client, HttpMethod.Post,
            $"/api/v1/collections/{owner.Collection}/folders", CollectionsCalls.Token(tokens, Guid.NewGuid(), "Owner"), TestContext.Current.CancellationToken,
            JsonContent.Create(new { name = "Planted" }, options: System.Text.Json.JsonSerializerOptions.Web));

        // The owner cannot nest a folder under a folder of another workspace either: that parent is absent for the owner.
        var foreignCollection = await CollectionsCalls.CreateCollectionAsync(client, owner.ForeignToken, "Foreign", TestContext.Current.CancellationToken);
        var foreignFolder = await CollectionsCalls.CreateFolderAsync(client, owner.ForeignToken, foreignCollection, "Foreign folder", null, TestContext.Current.CancellationToken);
        using var planted = await CollectionsCalls.SendAsync(client, HttpMethod.Post, $"/api/v1/collections/{owner.Collection}/folders", owner.Token,
            new { name = "Planted", parentFolderId = foreignFolder }, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, planted.StatusCode);

        var tree = await ReadTreeAsync(client, owner);
        Assert.Equal(new[] { "Auth" }, tree.Folders.Select(folder => folder.Name));
    }

    [Theory]
    [MemberData(nameof(Storage.Providers), MemberType = typeof(Storage))]
    public async Task Scoping_PUT_folders_id(string storage)
    {
        using var tokens = new TestTokens();
        await using var factory = Storage.Start(tokens, storage, postgres);
        await SchemaWait.AppliedAsync(factory, TestContext.Current.CancellationToken);
        using var client = factory.CreateClient();
        var owner = await OwnedRowsAsync(tokens, client);

        await ScopingAssertions.AssertNotFoundForOtherWorkspaceAsync(client, HttpMethod.Put,
            $"/api/v1/folders/{owner.Folder}", CollectionsCalls.Token(tokens, Guid.NewGuid(), "Owner"), TestContext.Current.CancellationToken,
            JsonContent.Create(new { name = "Hijacked" }, options: System.Text.Json.JsonSerializerOptions.Web));

        var tree = await ReadTreeAsync(client, owner);
        Assert.Equal(new[] { "Auth" }, tree.Folders.Select(folder => folder.Name));
    }

    [Theory]
    [MemberData(nameof(Storage.Providers), MemberType = typeof(Storage))]
    public async Task Scoping_POST_folders_id_move(string storage)
    {
        using var tokens = new TestTokens();
        await using var factory = Storage.Start(tokens, storage, postgres);
        await SchemaWait.AppliedAsync(factory, TestContext.Current.CancellationToken);
        using var client = factory.CreateClient();
        var owner = await OwnedRowsAsync(tokens, client);

        await ScopingAssertions.AssertNotFoundForOtherWorkspaceAsync(client, HttpMethod.Post,
            $"/api/v1/folders/{owner.Folder}/move", CollectionsCalls.Token(tokens, Guid.NewGuid(), "Owner"), TestContext.Current.CancellationToken,
            JsonContent.Create(new { parentFolderId = (Guid?)null, sortOrder = 0 }, options: System.Text.Json.JsonSerializerOptions.Web));

        // Moving the owner's folder under a folder of another workspace is refused as an absent parent.
        var foreignParent = await CollectionsCalls.CreateCollectionAsync(client, owner.ForeignToken, "Foreign", TestContext.Current.CancellationToken);
        var foreignFolder = await CollectionsCalls.CreateFolderAsync(client, owner.ForeignToken, foreignParent, "Foreign folder", null, TestContext.Current.CancellationToken);
        using var intoForeign = await CollectionsCalls.SendAsync(client, HttpMethod.Post, $"/api/v1/folders/{owner.Folder}/move", owner.Token,
            new { parentFolderId = foreignFolder }, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, intoForeign.StatusCode);
    }

    [Theory]
    [MemberData(nameof(Storage.Providers), MemberType = typeof(Storage))]
    public async Task Scoping_DELETE_folders_id(string storage)
    {
        using var tokens = new TestTokens();
        await using var factory = Storage.Start(tokens, storage, postgres);
        await SchemaWait.AppliedAsync(factory, TestContext.Current.CancellationToken);
        using var client = factory.CreateClient();
        var owner = await OwnedRowsAsync(tokens, client);

        await ScopingAssertions.AssertNotFoundForOtherWorkspaceAsync(client, HttpMethod.Delete,
            $"/api/v1/folders/{owner.Folder}", CollectionsCalls.Token(tokens, Guid.NewGuid(), "Owner"), TestContext.Current.CancellationToken);

        var tree = await ReadTreeAsync(client, owner);
        Assert.Equal(new[] { "Auth" }, tree.Folders.Select(folder => folder.Name));
    }

    /// <summary>One collection and one folder in a fresh workspace, and a token of a second workspace for the foreign calls.</summary>
    private static async Task<OwnedRows> OwnedRowsAsync(TestTokens tokens, HttpClient client)
    {
        var token = CollectionsCalls.Token(tokens, Guid.NewGuid(), "Member");
        var collection = await CollectionsCalls.CreateCollectionAsync(client, token, "Payments", TestContext.Current.CancellationToken);
        var folder = await CollectionsCalls.CreateFolderAsync(client, token, collection, "Auth", null, TestContext.Current.CancellationToken);
        return new OwnedRows(token, CollectionsCalls.Token(tokens, Guid.NewGuid(), "Member"), collection, folder);
    }

    private static async Task<CollectionTree> ReadTreeAsync(HttpClient client, OwnedRows owner)
    {
        using var response = await CollectionsCalls.SendAsync(client, HttpMethod.Get, $"/api/v1/collections/{owner.Collection}", owner.Token, cancellationToken: TestContext.Current.CancellationToken);
        return (await response.Content.ReadFromJsonAsync<CollectionTree>(TestContext.Current.CancellationToken))!;
    }

    private sealed record OwnedRows(string Token, string ForeignToken, Guid Collection, Guid Folder);
}
