using System.Net;
using PostmanClone.CollectionsService.Tests.Support;
using PostmanClone.TestKit;
using Xunit;

namespace PostmanClone.CollectionsService.Tests.Scoping;

/// <summary>
/// Tenant isolation for the Postman export (CONTRACTS §0, README §4): a collection of another workspace answers
/// <c>404 not_found</c>, never 403, and the export of the owner is unaffected. The import has no id, so it has no scoping test.
/// </summary>
public sealed class PostmanIoScopingTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [MemberData(nameof(Storage.Providers), MemberType = typeof(Storage))]
    public async Task Scoping_GET_collections_id_export_postman(string storage)
    {
        using var tokens = new TestTokens();
        await using var factory = Storage.Start(tokens, storage, postgres);
        await SchemaWait.AppliedAsync(factory, Ct);
        using var client = factory.CreateClient();
        var owner = CollectionsCalls.Token(tokens, Guid.NewGuid(), "Owner");
        var imported = await PostmanCalls.ImportedAsync(client, owner, PostmanFixtures.Read("minimal.json"), Ct);

        await ScopingAssertions.AssertNotFoundForOtherWorkspaceAsync(client, HttpMethod.Get,
            $"/api/v1/collections/{imported.CollectionId}/export/postman", CollectionsCalls.Token(tokens, Guid.NewGuid(), "Owner"), Ct);

        using var ownerExport = await PostmanCalls.ExportAsync(client, owner, imported.CollectionId, Ct);
        Assert.Equal(HttpStatusCode.OK, ownerExport.StatusCode);
    }
}
