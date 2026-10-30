using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using PostmanClone.CollectionsService.Api;
using PostmanClone.CollectionsService.Tests.Support;
using PostmanClone.TestKit;
using Xunit;

namespace PostmanClone.CollectionsService.Tests.Authorization;

/// <summary>
/// The workspace comes from the token's <c>organization</c> claim and nowhere else (CONTRACTS §0). These tests reach the
/// gate through the real pipeline, the group the domain routes will sit in.
/// </summary>
public sealed class WorkspaceGateTests
{
    [Fact]
    public async Task A_token_with_no_organization_claim_is_forbidden_as_no_active_workspace()
    {
        using var tokens = new TestTokens();
        await using var factory = new CollectionsFactory(tokens);
        await SchemaWait.AppliedAsync(factory, TestContext.Current.CancellationToken);

        using var response = await ProbeAsync(factory, tokens.Issue(), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("no_active_workspace", await ProblemJson.CodeAsync(response, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task A_token_naming_two_organizations_is_forbidden_as_multiple_workspaces()
    {
        using var tokens = new TestTokens();
        await using var factory = new CollectionsFactory(tokens);
        await SchemaWait.AppliedAsync(factory, TestContext.Current.CancellationToken);
        var claims = TestTokens.Membership(Guid.NewGuid(), "Member").Concat(TestTokens.Membership(Guid.NewGuid(), "Member"));

        using var response = await ProbeAsync(factory, tokens.Issue(claims), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("multiple_workspaces", await ProblemJson.CodeAsync(response, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task A_token_with_an_organization_but_no_role_for_it_is_forbidden_as_no_active_workspace()
    {
        using var tokens = new TestTokens();
        await using var factory = new CollectionsFactory(tokens);
        await SchemaWait.AppliedAsync(factory, TestContext.Current.CancellationToken);
        var organization = Guid.NewGuid();
        var claims = new[] { new Claim("organization", organization.ToString()) };

        using var response = await ProbeAsync(factory, tokens.Issue(claims), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("no_active_workspace", await ProblemJson.CodeAsync(response, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task A_token_naming_one_workspace_reaches_the_route_with_that_workspace_and_role()
    {
        using var tokens = new TestTokens();
        await using var factory = new CollectionsFactory(tokens);
        await SchemaWait.AppliedAsync(factory, TestContext.Current.CancellationToken);
        var workspace = Guid.NewGuid();

        using var response = await ProbeAsync(factory, tokens.Issue(TestTokens.Membership(workspace, "Admin")), TestContext.Current.CancellationToken);
        var probe = await response.Content.ReadFromJsonAsync<CollectionsApi.WorkspaceProbe>(TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(workspace, probe?.WorkspaceId);
        Assert.Equal("Admin", probe?.Role);
    }

    [Fact]
    public async Task A_workspace_named_in_the_query_is_ignored_in_favour_of_the_token()
    {
        using var tokens = new TestTokens();
        await using var factory = new CollectionsFactory(tokens);
        await SchemaWait.AppliedAsync(factory, TestContext.Current.CancellationToken);
        var own = Guid.NewGuid();
        var other = Guid.NewGuid();

        using var response = await ProbeAsync(
            factory,
            tokens.Issue(TestTokens.Membership(own, "Member")),
            TestContext.Current.CancellationToken,
            query: $"?workspaceId={other}");
        var probe = await response.Content.ReadFromJsonAsync<CollectionsApi.WorkspaceProbe>(TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(own, probe?.WorkspaceId);
    }

    private static async Task<HttpResponseMessage> ProbeAsync(
        CollectionsFactory factory,
        string token,
        CancellationToken cancellationToken,
        string query = "")
    {
        // The client is not disposed here: the caller reads the response body after this method returns.
        var client = factory.CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/internal/workspace" + query);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await client.SendAsync(request, cancellationToken);
    }
}
