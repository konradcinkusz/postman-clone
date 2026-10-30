using System.Net;
using System.Net.Http.Json;
using PostmanClone.CollectionsService.Contracts;
using PostmanClone.CollectionsService.Domain;
using PostmanClone.CollectionsService.Tests.Support;
using PostmanClone.TestKit;
using Xunit;

namespace PostmanClone.CollectionsService.Tests.Folders;

/// <summary>The folder routes of CONTRACTS §2: nesting, sort order, moves, cycles and deletes, against each provider.</summary>
public sealed class FolderEndpointTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    [Theory]
    [MemberData(nameof(Storage.Providers), MemberType = typeof(Storage))]
    public async Task Create_appends_each_new_sibling_after_the_last(string storage)
    {
        using var tokens = new TestTokens();
        await using var factory = Storage.Start(tokens, storage, postgres);
        await SchemaWait.AppliedAsync(factory, TestContext.Current.CancellationToken);
        using var client = factory.CreateClient();
        var token = CollectionsCalls.Token(tokens, Guid.NewGuid(), "Member");
        var collection = await CollectionsCalls.CreateCollectionAsync(client, token, "Payments", TestContext.Current.CancellationToken);

        using var response = await CollectionsCalls.SendAsync(client, HttpMethod.Post, $"/api/v1/collections/{collection}/folders", token,
            new { name = "Auth", parentFolderId = (Guid?)null }, TestContext.Current.CancellationToken);
        var first = await response.Content.ReadFromJsonAsync<FolderDto>(TestContext.Current.CancellationToken);
        var second = await CollectionsCalls.CreateFolderAsync(client, token, collection, "Refunds", null, TestContext.Current.CancellationToken);
        var tree = await ReadTreeAsync(client, token, collection);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal($"/api/v1/folders/{first!.Id}", response.Headers.Location?.OriginalString);
        Assert.Equal(collection, first.CollectionId);
        Assert.Equal(new[] { ("Auth", 0), ("Refunds", 1) }, tree.Folders.Select(folder => (folder.Name, folder.SortOrder)));
        Assert.Equal(second, tree.Folders[1].Id);
    }

    [Theory]
    [MemberData(nameof(Storage.Providers), MemberType = typeof(Storage))]
    public async Task Create_nests_eight_levels_and_refuses_the_ninth(string storage)
    {
        using var tokens = new TestTokens();
        await using var factory = Storage.Start(tokens, storage, postgres);
        await SchemaWait.AppliedAsync(factory, TestContext.Current.CancellationToken);
        using var client = factory.CreateClient();
        var token = CollectionsCalls.Token(tokens, Guid.NewGuid(), "Member");
        var collection = await CollectionsCalls.CreateCollectionAsync(client, token, "Payments", TestContext.Current.CancellationToken);
        Guid? parent = null;
        for (var level = 1; level <= CollectionLimits.MaxFolderDepth; level++)
        {
            parent = await CollectionsCalls.CreateFolderAsync(client, token, collection, $"Level {level}", parent, TestContext.Current.CancellationToken);
        }

        using var ninth = await CollectionsCalls.SendAsync(client, HttpMethod.Post, $"/api/v1/collections/{collection}/folders", token,
            new { name = "Level 9", parentFolderId = parent }, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, ninth.StatusCode);
        Assert.Equal("depth_exceeded", await ProblemJson.CodeAsync(ninth, TestContext.Current.CancellationToken));
    }

    [Theory]
    [MemberData(nameof(Storage.Providers), MemberType = typeof(Storage))]
    public async Task Create_refuses_a_parent_that_belongs_to_another_collection_of_the_same_workspace(string storage)
    {
        using var tokens = new TestTokens();
        await using var factory = Storage.Start(tokens, storage, postgres);
        await SchemaWait.AppliedAsync(factory, TestContext.Current.CancellationToken);
        using var client = factory.CreateClient();
        var token = CollectionsCalls.Token(tokens, Guid.NewGuid(), "Member");
        var payments = await CollectionsCalls.CreateCollectionAsync(client, token, "Payments", TestContext.Current.CancellationToken);
        var refunds = await CollectionsCalls.CreateCollectionAsync(client, token, "Refunds", TestContext.Current.CancellationToken);
        var foreignParent = await CollectionsCalls.CreateFolderAsync(client, token, payments, "Auth", null, TestContext.Current.CancellationToken);

        using var response = await CollectionsCalls.SendAsync(client, HttpMethod.Post, $"/api/v1/collections/{refunds}/folders", token,
            new { name = "Nested", parentFolderId = foreignParent }, TestContext.Current.CancellationToken);
        using var json = await CollectionsCalls.JsonAsync(response, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("validation_failed", json.RootElement.GetProperty("code").GetString());
        Assert.True(json.RootElement.GetProperty("errors").TryGetProperty("parentFolderId", out _));
    }

    [Theory]
    [MemberData(nameof(Storage.Providers), MemberType = typeof(Storage))]
    public async Task Create_refuses_the_501st_folder_of_a_collection_with_conflict(string storage)
    {
        using var tokens = new TestTokens();
        await using var factory = Storage.Start(tokens, storage, postgres);
        await SchemaWait.AppliedAsync(factory, TestContext.Current.CancellationToken);
        using var client = factory.CreateClient();
        var workspace = Guid.NewGuid();
        var token = CollectionsCalls.Token(tokens, workspace, "Member");
        var collection = await CollectionsCalls.CreateCollectionAsync(client, token, "Payments", TestContext.Current.CancellationToken);
        await Storage.AddFoldersAsync(factory, workspace, collection, CollectionLimits.MaxFoldersPerCollection, TestContext.Current.CancellationToken);

        using var response = await CollectionsCalls.SendAsync(client, HttpMethod.Post, $"/api/v1/collections/{collection}/folders", token,
            new { name = "One too many" }, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("conflict", await ProblemJson.CodeAsync(response, TestContext.Current.CancellationToken));
    }

    [Theory]
    [MemberData(nameof(Storage.Providers), MemberType = typeof(Storage))]
    public async Task Rename_changes_the_name_and_keeps_the_position(string storage)
    {
        using var tokens = new TestTokens();
        await using var factory = Storage.Start(tokens, storage, postgres);
        await SchemaWait.AppliedAsync(factory, TestContext.Current.CancellationToken);
        using var client = factory.CreateClient();
        var token = CollectionsCalls.Token(tokens, Guid.NewGuid(), "Member");
        var collection = await CollectionsCalls.CreateCollectionAsync(client, token, "Payments", TestContext.Current.CancellationToken);
        await CollectionsCalls.CreateFolderAsync(client, token, collection, "First", null, TestContext.Current.CancellationToken);
        var second = await CollectionsCalls.CreateFolderAsync(client, token, collection, "Second", null, TestContext.Current.CancellationToken);

        using var response = await CollectionsCalls.SendAsync(client, HttpMethod.Put, $"/api/v1/folders/{second}", token,
            new { name = "Renamed" }, TestContext.Current.CancellationToken);
        var renamed = await response.Content.ReadFromJsonAsync<FolderDto>(TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Renamed", renamed!.Name);
        Assert.Equal(1, renamed.SortOrder);
    }

    [Theory]
    [MemberData(nameof(Storage.Providers), MemberType = typeof(Storage))]
    public async Task Move_to_a_requested_position_renumbers_the_siblings_from_zero(string storage)
    {
        using var tokens = new TestTokens();
        await using var factory = Storage.Start(tokens, storage, postgres);
        await SchemaWait.AppliedAsync(factory, TestContext.Current.CancellationToken);
        using var client = factory.CreateClient();
        var token = CollectionsCalls.Token(tokens, Guid.NewGuid(), "Member");
        var collection = await CollectionsCalls.CreateCollectionAsync(client, token, "Payments", TestContext.Current.CancellationToken);
        await CollectionsCalls.CreateFolderAsync(client, token, collection, "Alpha", null, TestContext.Current.CancellationToken);
        await CollectionsCalls.CreateFolderAsync(client, token, collection, "Beta", null, TestContext.Current.CancellationToken);
        var gamma = await CollectionsCalls.CreateFolderAsync(client, token, collection, "Gamma", null, TestContext.Current.CancellationToken);

        using var response = await CollectionsCalls.SendAsync(client, HttpMethod.Post, $"/api/v1/folders/{gamma}/move", token,
            new { parentFolderId = (Guid?)null, sortOrder = 0 }, TestContext.Current.CancellationToken);
        var tree = await ReadTreeAsync(client, token, collection);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(new[] { ("Gamma", 0), ("Alpha", 1), ("Beta", 2) }, tree.Folders.Select(folder => (folder.Name, folder.SortOrder)));
    }

    [Theory]
    [MemberData(nameof(Storage.Providers), MemberType = typeof(Storage))]
    public async Task Move_under_another_parent_places_it_last_and_closes_the_gap_it_left(string storage)
    {
        using var tokens = new TestTokens();
        await using var factory = Storage.Start(tokens, storage, postgres);
        await SchemaWait.AppliedAsync(factory, TestContext.Current.CancellationToken);
        using var client = factory.CreateClient();
        var token = CollectionsCalls.Token(tokens, Guid.NewGuid(), "Member");
        var collection = await CollectionsCalls.CreateCollectionAsync(client, token, "Payments", TestContext.Current.CancellationToken);
        var alpha = await CollectionsCalls.CreateFolderAsync(client, token, collection, "Alpha", null, TestContext.Current.CancellationToken);
        var beta = await CollectionsCalls.CreateFolderAsync(client, token, collection, "Beta", null, TestContext.Current.CancellationToken);
        await CollectionsCalls.CreateFolderAsync(client, token, collection, "Inside", beta, TestContext.Current.CancellationToken);

        using var response = await CollectionsCalls.SendAsync(client, HttpMethod.Post, $"/api/v1/folders/{alpha}/move", token,
            new { parentFolderId = beta }, TestContext.Current.CancellationToken);
        var tree = await ReadTreeAsync(client, token, collection);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(new[] { ("Beta", 0) }, tree.Folders.Where(folder => folder.ParentFolderId is null).Select(folder => (folder.Name, folder.SortOrder)));
        Assert.Equal(new[] { ("Inside", 0), ("Alpha", 1) }, tree.Folders.Where(folder => folder.ParentFolderId == beta).Select(folder => (folder.Name, folder.SortOrder)));
    }

    [Theory]
    [MemberData(nameof(Storage.Providers), MemberType = typeof(Storage))]
    public async Task Move_clamps_a_position_past_the_end_to_the_end(string storage)
    {
        using var tokens = new TestTokens();
        await using var factory = Storage.Start(tokens, storage, postgres);
        await SchemaWait.AppliedAsync(factory, TestContext.Current.CancellationToken);
        using var client = factory.CreateClient();
        var token = CollectionsCalls.Token(tokens, Guid.NewGuid(), "Member");
        var collection = await CollectionsCalls.CreateCollectionAsync(client, token, "Payments", TestContext.Current.CancellationToken);
        var alpha = await CollectionsCalls.CreateFolderAsync(client, token, collection, "Alpha", null, TestContext.Current.CancellationToken);
        await CollectionsCalls.CreateFolderAsync(client, token, collection, "Beta", null, TestContext.Current.CancellationToken);

        using var response = await CollectionsCalls.SendAsync(client, HttpMethod.Post, $"/api/v1/folders/{alpha}/move", token,
            new { parentFolderId = (Guid?)null, sortOrder = 99 }, TestContext.Current.CancellationToken);
        var tree = await ReadTreeAsync(client, token, collection);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(new[] { ("Beta", 0), ("Alpha", 1) }, tree.Folders.Select(folder => (folder.Name, folder.SortOrder)));
    }

    [Theory]
    [MemberData(nameof(Storage.Providers), MemberType = typeof(Storage))]
    public async Task Move_into_itself_or_into_its_own_subfolder_is_a_cycle(string storage)
    {
        using var tokens = new TestTokens();
        await using var factory = Storage.Start(tokens, storage, postgres);
        await SchemaWait.AppliedAsync(factory, TestContext.Current.CancellationToken);
        using var client = factory.CreateClient();
        var token = CollectionsCalls.Token(tokens, Guid.NewGuid(), "Member");
        var collection = await CollectionsCalls.CreateCollectionAsync(client, token, "Payments", TestContext.Current.CancellationToken);
        var alpha = await CollectionsCalls.CreateFolderAsync(client, token, collection, "Alpha", null, TestContext.Current.CancellationToken);
        var beta = await CollectionsCalls.CreateFolderAsync(client, token, collection, "Beta", alpha, TestContext.Current.CancellationToken);
        var gamma = await CollectionsCalls.CreateFolderAsync(client, token, collection, "Gamma", beta, TestContext.Current.CancellationToken);

        using var intoItself = await CollectionsCalls.SendAsync(client, HttpMethod.Post, $"/api/v1/folders/{alpha}/move", token,
            new { parentFolderId = alpha }, TestContext.Current.CancellationToken);
        using var intoDescendant = await CollectionsCalls.SendAsync(client, HttpMethod.Post, $"/api/v1/folders/{alpha}/move", token,
            new { parentFolderId = gamma }, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, intoItself.StatusCode);
        Assert.Equal("cycle_detected", await ProblemJson.CodeAsync(intoItself, TestContext.Current.CancellationToken));
        Assert.Equal("cycle_detected", await ProblemJson.CodeAsync(intoDescendant, TestContext.Current.CancellationToken));
    }

    [Theory]
    [MemberData(nameof(Storage.Providers), MemberType = typeof(Storage))]
    public async Task Move_that_would_put_the_deepest_subfolder_past_level_eight_is_refused(string storage)
    {
        using var tokens = new TestTokens();
        await using var factory = Storage.Start(tokens, storage, postgres);
        await SchemaWait.AppliedAsync(factory, TestContext.Current.CancellationToken);
        using var client = factory.CreateClient();
        var workspace = Guid.NewGuid();
        var token = CollectionsCalls.Token(tokens, workspace, "Member");
        var collection = await CollectionsCalls.CreateCollectionAsync(client, token, "Payments", TestContext.Current.CancellationToken);
        var top = await Storage.AddFolderChainAsync(factory, workspace, collection, 2, TestContext.Current.CancellationToken);
        var chain = await Storage.AddFolderChainAsync(factory, workspace, collection, 7, TestContext.Current.CancellationToken);

        // The 7-level chain under level 2 would reach level 9. Under level 1 it reaches level 8, which is allowed.
        using var tooDeep = await CollectionsCalls.SendAsync(client, HttpMethod.Post, $"/api/v1/folders/{chain[0]}/move", token,
            new { parentFolderId = top[1] }, TestContext.Current.CancellationToken);
        using var fits = await CollectionsCalls.SendAsync(client, HttpMethod.Post, $"/api/v1/folders/{chain[0]}/move", token,
            new { parentFolderId = top[0] }, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, tooDeep.StatusCode);
        Assert.Equal("depth_exceeded", await ProblemJson.CodeAsync(tooDeep, TestContext.Current.CancellationToken));
        Assert.Equal(HttpStatusCode.OK, fits.StatusCode);
    }

    [Theory]
    [MemberData(nameof(Storage.Providers), MemberType = typeof(Storage))]
    public async Task Move_to_another_collection_of_the_same_workspace_is_refused(string storage)
    {
        using var tokens = new TestTokens();
        await using var factory = Storage.Start(tokens, storage, postgres);
        await SchemaWait.AppliedAsync(factory, TestContext.Current.CancellationToken);
        using var client = factory.CreateClient();
        var token = CollectionsCalls.Token(tokens, Guid.NewGuid(), "Member");
        var payments = await CollectionsCalls.CreateCollectionAsync(client, token, "Payments", TestContext.Current.CancellationToken);
        var refunds = await CollectionsCalls.CreateCollectionAsync(client, token, "Refunds", TestContext.Current.CancellationToken);
        var alpha = await CollectionsCalls.CreateFolderAsync(client, token, payments, "Alpha", null, TestContext.Current.CancellationToken);
        var otherRoot = await CollectionsCalls.CreateFolderAsync(client, token, refunds, "Other", null, TestContext.Current.CancellationToken);

        using var response = await CollectionsCalls.SendAsync(client, HttpMethod.Post, $"/api/v1/folders/{alpha}/move", token,
            new { parentFolderId = otherRoot }, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("validation_failed", await ProblemJson.CodeAsync(response, TestContext.Current.CancellationToken));
    }

    [Theory]
    [MemberData(nameof(Storage.Providers), MemberType = typeof(Storage))]
    public async Task A_chain_far_deeper_than_the_limit_is_checked_for_a_cycle_without_recursion(string storage)
    {
        using var tokens = new TestTokens();
        await using var factory = Storage.Start(tokens, storage, postgres);
        await SchemaWait.AppliedAsync(factory, TestContext.Current.CancellationToken);
        using var client = factory.CreateClient();
        var workspace = Guid.NewGuid();
        var token = CollectionsCalls.Token(tokens, workspace, "Member");
        var collection = await CollectionsCalls.CreateCollectionAsync(client, token, "Deep", TestContext.Current.CancellationToken);
        var chain = await Storage.AddFolderChainAsync(factory, workspace, collection, 300, TestContext.Current.CancellationToken);

        // The top of the chain under its own bottom: a cycle, found by walking the 300 levels, not by recursing through them.
        using var response = await CollectionsCalls.SendAsync(client, HttpMethod.Post, $"/api/v1/folders/{chain[0]}/move", token,
            new { parentFolderId = chain[^1] }, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("cycle_detected", await ProblemJson.CodeAsync(response, TestContext.Current.CancellationToken));
    }

    [Theory]
    [MemberData(nameof(Storage.Providers), MemberType = typeof(Storage))]
    public async Task Delete_removes_the_folder_and_its_subfolders_and_closes_the_gap(string storage)
    {
        using var tokens = new TestTokens();
        await using var factory = Storage.Start(tokens, storage, postgres);
        await SchemaWait.AppliedAsync(factory, TestContext.Current.CancellationToken);
        using var client = factory.CreateClient();
        var token = CollectionsCalls.Token(tokens, Guid.NewGuid(), "Member");
        var collection = await CollectionsCalls.CreateCollectionAsync(client, token, "Payments", TestContext.Current.CancellationToken);
        var alpha = await CollectionsCalls.CreateFolderAsync(client, token, collection, "Alpha", null, TestContext.Current.CancellationToken);
        await CollectionsCalls.CreateFolderAsync(client, token, collection, "Inside", alpha, TestContext.Current.CancellationToken);
        await CollectionsCalls.CreateFolderAsync(client, token, collection, "Beta", null, TestContext.Current.CancellationToken);

        using var response = await CollectionsCalls.SendAsync(client, HttpMethod.Delete, $"/api/v1/folders/{alpha}", token, cancellationToken: TestContext.Current.CancellationToken);
        var tree = await ReadTreeAsync(client, token, collection);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(new[] { ("Beta", 0) }, tree.Folders.Select(folder => (folder.Name, folder.SortOrder)));
    }

    [Theory]
    [MemberData(nameof(Storage.Providers), MemberType = typeof(Storage))]
    public async Task Delete_of_a_folder_that_is_not_there_is_not_found(string storage)
    {
        using var tokens = new TestTokens();
        await using var factory = Storage.Start(tokens, storage, postgres);
        await SchemaWait.AppliedAsync(factory, TestContext.Current.CancellationToken);
        using var client = factory.CreateClient();
        var token = CollectionsCalls.Token(tokens, Guid.NewGuid(), "Member");

        using var response = await CollectionsCalls.SendAsync(client, HttpMethod.Delete, $"/api/v1/folders/{Guid.NewGuid()}", token, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("not_found", await ProblemJson.CodeAsync(response, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Deleting_a_folder_removes_the_saved_requests_inside_it()
    {
        // The requests go with the foreign-key cascade, which only the PostgreSQL provider applies (the InMemory provider
        // does not cascade rows it has not loaded), so this test runs on PostgreSQL alone.
        using var tokens = new TestTokens();
        await using var factory = Storage.Start(tokens, Storage.PostgreSql, postgres);
        await SchemaWait.AppliedAsync(factory, TestContext.Current.CancellationToken);
        using var client = factory.CreateClient();
        var workspace = Guid.NewGuid();
        var token = CollectionsCalls.Token(tokens, workspace, "Member");
        var collection = await CollectionsCalls.CreateCollectionAsync(client, token, "Payments", TestContext.Current.CancellationToken);
        var folder = await CollectionsCalls.CreateFolderAsync(client, token, collection, "Auth", null, TestContext.Current.CancellationToken);
        await Storage.AddRequestAsync(factory, workspace, collection, folder, "POST", TestContext.Current.CancellationToken);

        using var response = await CollectionsCalls.SendAsync(client, HttpMethod.Delete, $"/api/v1/folders/{folder}", token, cancellationToken: TestContext.Current.CancellationToken);
        var tree = await ReadTreeAsync(client, token, collection);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Empty(tree.Requests);
    }

    private static async Task<CollectionTree> ReadTreeAsync(HttpClient client, string token, Guid collection)
    {
        using var response = await CollectionsCalls.SendAsync(client, HttpMethod.Get, $"/api/v1/collections/{collection}", token, cancellationToken: TestContext.Current.CancellationToken);
        return (await response.Content.ReadFromJsonAsync<CollectionTree>(TestContext.Current.CancellationToken))!;
    }
}
