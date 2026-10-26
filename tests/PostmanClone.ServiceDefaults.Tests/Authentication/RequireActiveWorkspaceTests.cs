using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using PostmanClone.ServiceDefaults.Authentication;
using PostmanClone.TestKit;
using Xunit;

namespace PostmanClone.ServiceDefaults.Tests.Authentication;

/// <summary>The domain endpoint filter: one active workspace passes, none or several answer 403 with a stable code (CONTRACTS §0).</summary>
public sealed class RequireActiveWorkspaceTests
{
    [Fact]
    public async Task A_token_with_one_organization_passes_the_filter()
    {
        using var tokens = new TestTokens();
        await using var app = await SecureTestHost.StartAsync(tokens, map: MapWorkspace);
        using var client = TestHost.ClientFor(app);

        using var response = await GetWorkspaceAsync(client, tokens.Issue(TestTokens.Membership(Guid.NewGuid(), WorkspaceRoles.Member)));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task A_token_with_no_organization_answers_403_no_active_workspace()
    {
        using var tokens = new TestTokens();
        await using var app = await SecureTestHost.StartAsync(tokens, map: MapWorkspace);
        using var client = TestHost.ClientFor(app);

        using var response = await GetWorkspaceAsync(client, tokens.Issue());

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("no_active_workspace", await CodeAsync(response));
    }

    [Fact]
    public async Task A_token_with_two_organizations_answers_403_multiple_workspaces()
    {
        using var tokens = new TestTokens();
        await using var app = await SecureTestHost.StartAsync(tokens, map: MapWorkspace);
        using var client = TestHost.ClientFor(app);
        var claims = TestTokens.Membership(Guid.NewGuid(), WorkspaceRoles.Owner)
            .Concat(TestTokens.Membership(Guid.NewGuid(), WorkspaceRoles.Member));

        using var response = await GetWorkspaceAsync(client, tokens.Issue(claims));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("multiple_workspaces", await CodeAsync(response));
    }

    private static void MapWorkspace(WebApplication app) =>
        app.MapGet("/workspace", () => Results.Ok()).RequireActiveWorkspace();

    private static Task<HttpResponseMessage> GetWorkspaceAsync(HttpClient client, string token)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/workspace")
        {
            Headers = { Authorization = new AuthenticationHeaderValue("Bearer", token) },
        };
        return client.SendAsync(request, TestContext.Current.CancellationToken);
    }

    private static async Task<string?> CodeAsync(HttpResponseMessage response)
    {
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        return body.RootElement.GetProperty("code").GetString();
    }
}
