using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using PostmanClone.CollectionsService.Contracts;
using PostmanClone.CollectionsService.Domain;
using PostmanClone.CollectionsService.Tests.Support;
using PostmanClone.ServiceDefaults.Paging;
using PostmanClone.TestKit;
using Xunit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PostmanClone.CollectionsService.Data;

namespace PostmanClone.CollectionsService.Tests.Collections;

/// <summary>The collection routes of CONTRACTS §2, run against each provider.</summary>
public sealed class CollectionEndpointTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    [Theory]
    [MemberData(nameof(Storage.Providers), MemberType = typeof(Storage))]
    public async Task Create_returns_201_with_a_location_and_records_the_caller_and_the_clock(string storage)
    {
        using var tokens = new TestTokens();
        var clock = new Microsoft.Extensions.Time.Testing.FakeTimeProvider(DateTimeOffset.UtcNow);
        await using var factory = Storage.Start(tokens, storage, postgres, clock);
        await SchemaWait.AppliedAsync(factory, TestContext.Current.CancellationToken);
        using var client = factory.CreateClient();
        var token = CollectionsCalls.Token(tokens, Guid.NewGuid(), "Member");

        using var response = await CollectionsCalls.SendAsync(client, HttpMethod.Post, "/api/v1/collections", token,
            new { name = "  Payments  ", description = "Money movement" }, TestContext.Current.CancellationToken);
        var created = await response.Content.ReadFromJsonAsync<CollectionDto>(TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal($"/api/v1/collections/{created!.Id}", response.Headers.Location?.OriginalString);
        Assert.Equal("Payments", created.Name);
        Assert.Equal("Money movement", created.Description);
        Assert.Equal(CollectionsCalls.SubjectOf(token), created.CreatedBy);
        Assert.Equal(clock.GetUtcNow(), created.CreatedAt);
        Assert.Equal(created.CreatedAt, created.UpdatedAt);
    }

    [Theory]
    [MemberData(nameof(Storage.Providers), MemberType = typeof(Storage))]
    public async Task Create_refuses_a_blank_name_and_a_name_over_100_characters(string storage)
    {
        using var tokens = new TestTokens();
        await using var factory = Storage.Start(tokens, storage, postgres);
        await SchemaWait.AppliedAsync(factory, TestContext.Current.CancellationToken);
        using var client = factory.CreateClient();
        var token = CollectionsCalls.Token(tokens, Guid.NewGuid(), "Member");

        using var blank = await CollectionsCalls.SendAsync(client, HttpMethod.Post, "/api/v1/collections", token, new { name = "   " }, TestContext.Current.CancellationToken);
        using var tooLong = await CollectionsCalls.SendAsync(client, HttpMethod.Post, "/api/v1/collections", token, new { name = new string('x', 101) }, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, blank.StatusCode);
        Assert.Equal("validation_failed", await ProblemJson.CodeAsync(blank, TestContext.Current.CancellationToken));
        Assert.Equal(HttpStatusCode.BadRequest, tooLong.StatusCode);
        Assert.Equal("validation_failed", await ProblemJson.CodeAsync(tooLong, TestContext.Current.CancellationToken));
    }

    [Theory]
    [MemberData(nameof(Storage.Providers), MemberType = typeof(Storage))]
    public async Task Create_refuses_the_201st_collection_of_a_workspace_with_conflict(string storage)
    {
        using var tokens = new TestTokens();
        await using var factory = Storage.Start(tokens, storage, postgres);
        await SchemaWait.AppliedAsync(factory, TestContext.Current.CancellationToken);
        var workspace = Guid.NewGuid();
        await Storage.AddCollectionsAsync(factory, workspace, CollectionLimits.MaxCollectionsPerWorkspace, TestContext.Current.CancellationToken);
        using var client = factory.CreateClient();

        using var response = await CollectionsCalls.SendAsync(client, HttpMethod.Post, "/api/v1/collections", CollectionsCalls.Token(tokens, workspace, "Member"),
            new { name = "One too many" }, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("conflict", await ProblemJson.CodeAsync(response, TestContext.Current.CancellationToken));
    }

    [Theory]
    [MemberData(nameof(Storage.Providers), MemberType = typeof(Storage))]
    public async Task Get_returns_the_collection_with_its_folders_and_requests_flat(string storage)
    {
        using var tokens = new TestTokens();
        await using var factory = Storage.Start(tokens, storage, postgres);
        await SchemaWait.AppliedAsync(factory, TestContext.Current.CancellationToken);
        using var client = factory.CreateClient();
        var workspace = Guid.NewGuid();
        var token = CollectionsCalls.Token(tokens, workspace, "Member");
        var collection = await CollectionsCalls.CreateCollectionAsync(client, token, "Payments", TestContext.Current.CancellationToken);
        var folder = await CollectionsCalls.CreateFolderAsync(client, token, collection, "Auth", null, TestContext.Current.CancellationToken);
        await Storage.AddRequestAsync(factory, workspace, collection, folder, "POST", TestContext.Current.CancellationToken);

        using var response = await CollectionsCalls.SendAsync(client, HttpMethod.Get, $"/api/v1/collections/{collection}", token, cancellationToken: TestContext.Current.CancellationToken);
        var tree = await response.Content.ReadFromJsonAsync<CollectionTree>(TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(collection, tree!.Collection.Id);
        Assert.Equal(new[] { "Auth" }, tree.Folders.Select(item => item.Name));
        Assert.Equal(folder, tree.Folders[0].Id);
        Assert.Equal(new[] { "POST" }, tree.Requests.Select(request => request.Method));
        Assert.Equal(folder, tree.Requests[0].FolderId);
    }

    [Theory]
    [MemberData(nameof(Storage.Providers), MemberType = typeof(Storage))]
    public async Task Update_replaces_the_name_and_the_description(string storage)
    {
        using var tokens = new TestTokens();
        await using var factory = Storage.Start(tokens, storage, postgres);
        await SchemaWait.AppliedAsync(factory, TestContext.Current.CancellationToken);
        using var client = factory.CreateClient();
        var token = CollectionsCalls.Token(tokens, Guid.NewGuid(), "Member");
        var collection = await CollectionsCalls.CreateCollectionAsync(client, token, "Payments", TestContext.Current.CancellationToken);

        using var response = await CollectionsCalls.SendAsync(client, HttpMethod.Put, $"/api/v1/collections/{collection}", token,
            new { name = "Billing", description = (string?)null }, TestContext.Current.CancellationToken);
        var updated = await response.Content.ReadFromJsonAsync<CollectionDto>(TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Billing", updated!.Name);
        Assert.Null(updated.Description);
    }

    [Theory]
    [MemberData(nameof(Storage.Providers), MemberType = typeof(Storage))]
    public async Task Update_refuses_a_blank_name(string storage)
    {
        using var tokens = new TestTokens();
        await using var factory = Storage.Start(tokens, storage, postgres);
        await SchemaWait.AppliedAsync(factory, TestContext.Current.CancellationToken);
        using var client = factory.CreateClient();
        var token = CollectionsCalls.Token(tokens, Guid.NewGuid(), "Member");
        var collection = await CollectionsCalls.CreateCollectionAsync(client, token, "Payments", TestContext.Current.CancellationToken);

        using var response = await CollectionsCalls.SendAsync(client, HttpMethod.Put, $"/api/v1/collections/{collection}", token,
            new { name = "\t" }, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("validation_failed", await ProblemJson.CodeAsync(response, TestContext.Current.CancellationToken));
    }

    [Theory]
    [MemberData(nameof(Storage.Providers), MemberType = typeof(Storage))]
    public async Task Delete_by_the_creator_removes_the_collection(string storage)
    {
        using var tokens = new TestTokens();
        await using var factory = Storage.Start(tokens, storage, postgres);
        await SchemaWait.AppliedAsync(factory, TestContext.Current.CancellationToken);
        using var client = factory.CreateClient();
        var token = CollectionsCalls.Token(tokens, Guid.NewGuid(), "Member");
        var collection = await CollectionsCalls.CreateCollectionAsync(client, token, "Payments", TestContext.Current.CancellationToken);

        using var deleted = await CollectionsCalls.SendAsync(client, HttpMethod.Delete, $"/api/v1/collections/{collection}", token, cancellationToken: TestContext.Current.CancellationToken);
        using var after = await CollectionsCalls.SendAsync(client, HttpMethod.Get, $"/api/v1/collections/{collection}", token, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, after.StatusCode);
    }

    [Theory]
    [MemberData(nameof(Storage.Providers), MemberType = typeof(Storage))]
    public async Task Delete_by_a_member_who_did_not_create_it_is_forbidden_and_leaves_it_in_place(string storage)
    {
        using var tokens = new TestTokens();
        await using var factory = Storage.Start(tokens, storage, postgres);
        await SchemaWait.AppliedAsync(factory, TestContext.Current.CancellationToken);
        using var client = factory.CreateClient();
        var workspace = Guid.NewGuid();
        var creator = CollectionsCalls.Token(tokens, workspace, "Member");
        var collection = await CollectionsCalls.CreateCollectionAsync(client, creator, "Payments", TestContext.Current.CancellationToken);

        using var response = await CollectionsCalls.SendAsync(client, HttpMethod.Delete, $"/api/v1/collections/{collection}",
            CollectionsCalls.Token(tokens, workspace, "Member"), cancellationToken: TestContext.Current.CancellationToken);
        using var stillThere = await CollectionsCalls.SendAsync(client, HttpMethod.Get, $"/api/v1/collections/{collection}", creator, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("forbidden", await ProblemJson.CodeAsync(response, TestContext.Current.CancellationToken));
        Assert.Equal(HttpStatusCode.OK, stillThere.StatusCode);
    }

    [Theory]
    [MemberData(nameof(Storage.Providers), MemberType = typeof(Storage))]
    public async Task Delete_by_an_admin_or_an_owner_who_did_not_create_it_removes_it(string storage)
    {
        using var tokens = new TestTokens();
        await using var factory = Storage.Start(tokens, storage, postgres);
        await SchemaWait.AppliedAsync(factory, TestContext.Current.CancellationToken);
        using var client = factory.CreateClient();
        var workspace = Guid.NewGuid();
        var collection = await CollectionsCalls.CreateCollectionAsync(client, CollectionsCalls.Token(tokens, workspace, "Member"), "Payments", TestContext.Current.CancellationToken);

        using var byAdmin = await CollectionsCalls.SendAsync(client, HttpMethod.Delete, $"/api/v1/collections/{collection}",
            CollectionsCalls.Token(tokens, workspace, "Admin"), cancellationToken: TestContext.Current.CancellationToken);
        var second = await CollectionsCalls.CreateCollectionAsync(client, CollectionsCalls.Token(tokens, workspace, "Member"), "Refunds", TestContext.Current.CancellationToken);
        using var byOwner = await CollectionsCalls.SendAsync(client, HttpMethod.Delete, $"/api/v1/collections/{second}",
            CollectionsCalls.Token(tokens, workspace, "Owner"), cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, byAdmin.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, byOwner.StatusCode);
    }

    [Theory]
    [MemberData(nameof(Storage.Providers), MemberType = typeof(Storage))]
    public async Task List_pages_by_name_and_clamps_the_page_size(string storage)
    {
        using var tokens = new TestTokens();
        await using var factory = Storage.Start(tokens, storage, postgres);
        await SchemaWait.AppliedAsync(factory, TestContext.Current.CancellationToken);
        using var client = factory.CreateClient();
        var token = CollectionsCalls.Token(tokens, Guid.NewGuid(), "Member");
        foreach (var name in new[] { "Gamma", "Alpha", "Beta" })
        {
            await CollectionsCalls.CreateCollectionAsync(client, token, name, TestContext.Current.CancellationToken);
        }

        var second = await client.SendAsync(WithToken(HttpMethod.Get, "/api/v1/collections?page=2&pageSize=2", token), TestContext.Current.CancellationToken);
        var secondPage = await second.Content.ReadFromJsonAsync<PageResult<CollectionSummary>>(TestContext.Current.CancellationToken);
        var huge = await client.SendAsync(WithToken(HttpMethod.Get, "/api/v1/collections?pageSize=500", token), TestContext.Current.CancellationToken);
        var hugePage = await huge.Content.ReadFromJsonAsync<PageResult<CollectionSummary>>(TestContext.Current.CancellationToken);

        Assert.Equal(new[] { "Gamma" }, secondPage!.Items.Select(collection => collection.Name));
        Assert.Equal(3, secondPage.Total);
        Assert.Equal(2, secondPage.Page);
        Assert.Equal(2, secondPage.PageSize);
        Assert.Equal(PageRequest.MaxPageSize, hugePage!.PageSize);
    }

    [Theory]
    [MemberData(nameof(Storage.Providers), MemberType = typeof(Storage))]
    public async Task List_counts_the_saved_requests_of_each_collection(string storage)
    {
        using var tokens = new TestTokens();
        await using var factory = Storage.Start(tokens, storage, postgres);
        await SchemaWait.AppliedAsync(factory, TestContext.Current.CancellationToken);
        using var client = factory.CreateClient();
        var workspace = Guid.NewGuid();
        var token = CollectionsCalls.Token(tokens, workspace, "Member");
        var busy = await CollectionsCalls.CreateCollectionAsync(client, token, "Busy", TestContext.Current.CancellationToken);
        await CollectionsCalls.CreateCollectionAsync(client, token, "Empty", TestContext.Current.CancellationToken);
        await Storage.AddRequestAsync(factory, workspace, busy, null, "GET", TestContext.Current.CancellationToken);
        await Storage.AddRequestAsync(factory, workspace, busy, null, "PUT", TestContext.Current.CancellationToken);

        using var response = await CollectionsCalls.SendAsync(client, HttpMethod.Get, "/api/v1/collections", token, cancellationToken: TestContext.Current.CancellationToken);
        var page = await response.Content.ReadFromJsonAsync<PageResult<CollectionSummary>>(TestContext.Current.CancellationToken);

        Assert.Equal(2, page!.Items.Single(collection => collection.Name == "Busy").RequestCount);
        Assert.Equal(0, page.Items.Single(collection => collection.Name == "Empty").RequestCount);
    }

    [Fact]
    public async Task Deleting_a_collection_removes_its_folders_and_requests_on_postgresql()
    {
        // The foreign-key cascade does this on PostgreSQL alone; the InMemory provider keeps rows the deleted parent no longer has.
        using var tokens = new TestTokens();
        await using var factory = Storage.Start(tokens, Storage.PostgreSql, postgres);
        await SchemaWait.AppliedAsync(factory, TestContext.Current.CancellationToken);
        using var client = factory.CreateClient();
        var workspace = Guid.NewGuid();
        var token = CollectionsCalls.Token(tokens, workspace, "Member");
        var collection = await CollectionsCalls.CreateCollectionAsync(client, token, "Payments", TestContext.Current.CancellationToken);
        var folder = await CollectionsCalls.CreateFolderAsync(client, token, collection, "Auth", null, TestContext.Current.CancellationToken);
        await Storage.AddRequestAsync(factory, workspace, collection, folder, "POST", TestContext.Current.CancellationToken);

        using var response = await CollectionsCalls.SendAsync(client, HttpMethod.Delete, $"/api/v1/collections/{collection}", token, cancellationToken: TestContext.Current.CancellationToken);
        using var folderAfter = await CollectionsCalls.SendAsync(client, HttpMethod.Delete, $"/api/v1/folders/{folder}", token, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, folderAfter.StatusCode);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CollectionsDbContext>();
        Assert.Equal(0, await db.SavedRequests.CountAsync(request => request.CollectionId == collection, TestContext.Current.CancellationToken));
    }

    private static HttpRequestMessage WithToken(HttpMethod method, string url, string token)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return request;
    }
}
