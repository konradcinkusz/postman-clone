using System.Net;
using PostmanClone.CollectionsService.Domain;
using PostmanClone.CollectionsService.Tests.Support;
using PostmanClone.TestKit;
using Xunit;
using Microsoft.Extensions.DependencyInjection;
using PostmanClone.CollectionsService.Data;

namespace PostmanClone.CollectionsService.Tests.Ordering;

/// <summary>
/// Concurrent writers must not interleave: two folders created at once under one parent get two positions, and two
/// collections created at once in one workspace count the limit one after the other. These need real row locks, so
/// they run on PostgreSQL only.
/// </summary>
public sealed class SortOrderConcurrencyTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    private const int Parallel = 8;

    [Fact]
    public async Task Folders_created_at_once_under_one_parent_get_distinct_consecutive_positions()
    {
        using var tokens = new TestTokens();
        await using var factory = Storage.Start(tokens, Storage.PostgreSql, postgres);
        await SchemaWait.AppliedAsync(factory, TestContext.Current.CancellationToken);
        using var client = factory.CreateClient();
        var token = CollectionsCalls.Token(tokens, Guid.NewGuid(), "Member");
        var collection = await CollectionsCalls.CreateCollectionAsync(client, token, "Payments", TestContext.Current.CancellationToken);
        var parent = await CollectionsCalls.CreateFolderAsync(client, token, collection, "Parent", null, TestContext.Current.CancellationToken);

        var responses = await Task.WhenAll(Enumerable.Range(0, Parallel).Select(index =>
            CollectionsCalls.SendAsync(client, HttpMethod.Post, $"/api/v1/collections/{collection}/folders", token,
                new { name = $"Child {index}", parentFolderId = parent }, TestContext.Current.CancellationToken)));
        foreach (var response in responses)
        {
            response.Dispose();
        }

        Assert.All(responses, response => Assert.Equal(HttpStatusCode.Created, response.StatusCode));
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CollectionsDbContext>();
        var positions = db.Folders
            .Where(folder => folder.ParentFolderId == parent)
            .Select(folder => folder.SortOrder)
            .ToList();
        Assert.Equal(Enumerable.Range(0, Parallel).ToList(), positions.Order().ToList());
    }

    [Fact]
    public async Task Collections_created_at_once_in_one_workspace_respect_the_workspace_limit()
    {
        using var tokens = new TestTokens();
        await using var factory = Storage.Start(tokens, Storage.PostgreSql, postgres);
        await SchemaWait.AppliedAsync(factory, TestContext.Current.CancellationToken);
        using var client = factory.CreateClient();
        var workspace = Guid.NewGuid();
        var token = CollectionsCalls.Token(tokens, workspace, "Member");
        await Storage.AddCollectionsAsync(factory, workspace, CollectionLimits.MaxCollectionsPerWorkspace - 1, TestContext.Current.CancellationToken);

        var responses = await Task.WhenAll(Enumerable.Range(0, 2).Select(index =>
            CollectionsCalls.SendAsync(client, HttpMethod.Post, "/api/v1/collections", token, new { name = $"Race {index}" }, TestContext.Current.CancellationToken)));
        var statuses = responses.Select(response => response.StatusCode).Order().ToList();
        foreach (var response in responses)
        {
            response.Dispose();
        }

        // One fills the last place; the other sees the limit reached once the first has committed.
        Assert.Equal(new[] { HttpStatusCode.Created, HttpStatusCode.Conflict }, statuses);
    }
}
