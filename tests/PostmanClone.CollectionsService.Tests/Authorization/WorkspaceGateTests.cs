using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using PostmanClone.CollectionsService.Contracts;
using PostmanClone.CollectionsService.Tests.Support;
using PostmanClone.TestKit;
using Xunit;

namespace PostmanClone.CollectionsService.Tests.Authorization;

/// <summary>
/// The workspace comes from the token's <c>organization</c> claim and nowhere else (CONTRACTS §0). These tests reach the
/// gate through the first domain route, <c>GET /api/v1/collections</c>, which every domain route shares.
/// </summary>
public sealed class WorkspaceGateTests
{
    private const string ListUrl = "/api/v1/collections";

    [Fact]
    public async Task A_token_with_no_organization_claim_is_forbidden_as_no_active_workspace()
    {
        using var tokens = new TestTokens();
        await using var factory = new CollectionsFactory(tokens);
        await SchemaWait.AppliedAsync(factory, TestContext.Current.CancellationToken);
        using var client = factory.CreateClient();

        using var response = await CollectionsCalls.SendAsync(client, HttpMethod.Get, ListUrl, tokens.Issue(), cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("no_active_workspace", await ProblemJson.CodeAsync(response, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task A_token_naming_two_organizations_is_forbidden_as_multiple_workspaces()
    {
        using var tokens = new TestTokens();
        await using var factory = new CollectionsFactory(tokens);
        await SchemaWait.AppliedAsync(factory, TestContext.Current.CancellationToken);
        using var client = factory.CreateClient();
        var claims = TestTokens.Membership(Guid.NewGuid(), "Member").Concat(TestTokens.Membership(Guid.NewGuid(), "Member"));

        using var response = await CollectionsCalls.SendAsync(client, HttpMethod.Get, ListUrl, tokens.Issue(claims), cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("multiple_workspaces", await ProblemJson.CodeAsync(response, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task A_token_with_an_organization_but_no_role_for_it_is_forbidden_as_no_active_workspace()
    {
        using var tokens = new TestTokens();
        await using var factory = new CollectionsFactory(tokens);
        await SchemaWait.AppliedAsync(factory, TestContext.Current.CancellationToken);
        using var client = factory.CreateClient();
        var claims = new[] { new Claim("organization", Guid.NewGuid().ToString()) };

        using var response = await CollectionsCalls.SendAsync(client, HttpMethod.Get, ListUrl, tokens.Issue(claims), cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("no_active_workspace", await ProblemJson.CodeAsync(response, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task A_token_naming_one_workspace_lists_only_that_workspaces_collections()
    {
        using var tokens = new TestTokens();
        await using var factory = new CollectionsFactory(tokens);
        await SchemaWait.AppliedAsync(factory, TestContext.Current.CancellationToken);
        using var client = factory.CreateClient();
        var own = Guid.NewGuid();
        var other = Guid.NewGuid();
        var ownToken = CollectionsCalls.Token(tokens, own, "Member");
        await CollectionsCalls.CreateCollectionAsync(client, ownToken, "Mine", TestContext.Current.CancellationToken);
        await CollectionsCalls.CreateCollectionAsync(client, CollectionsCalls.Token(tokens, other, "Member"), "Theirs", TestContext.Current.CancellationToken);

        using var response = await CollectionsCalls.SendAsync(client, HttpMethod.Get, ListUrl, ownToken, cancellationToken: TestContext.Current.CancellationToken);
        var page = await response.Content.ReadFromJsonAsync<PageResult<CollectionSummary>>(TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(new[] { "Mine" }, page!.Items.Select(collection => collection.Name));
    }

    [Fact]
    public async Task A_workspace_named_in_the_query_is_ignored_in_favour_of_the_token()
    {
        using var tokens = new TestTokens();
        await using var factory = new CollectionsFactory(tokens);
        await SchemaWait.AppliedAsync(factory, TestContext.Current.CancellationToken);
        using var client = factory.CreateClient();
        var own = Guid.NewGuid();
        var other = Guid.NewGuid();
        var ownToken = CollectionsCalls.Token(tokens, own, "Member");
        await CollectionsCalls.CreateCollectionAsync(client, ownToken, "Mine", TestContext.Current.CancellationToken);
        await CollectionsCalls.CreateCollectionAsync(client, CollectionsCalls.Token(tokens, other, "Member"), "Theirs", TestContext.Current.CancellationToken);

        using var response = await CollectionsCalls.SendAsync(client, HttpMethod.Get, $"{ListUrl}?workspaceId={other}", ownToken,
            cancellationToken: TestContext.Current.CancellationToken);
        var page = await response.Content.ReadFromJsonAsync<PageResult<CollectionSummary>>(TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.DoesNotContain("Theirs", page!.Items.Select(collection => collection.Name));
    }
}
