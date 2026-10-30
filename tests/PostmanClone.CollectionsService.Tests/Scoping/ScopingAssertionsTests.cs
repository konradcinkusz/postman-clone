using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using PostmanClone.CollectionsService.Tests.Support;
using PostmanClone.ServiceDefaults.Authentication;
using PostmanClone.TestKit;
using Xunit;

namespace PostmanClone.CollectionsService.Tests.Scoping;

/// <summary>
/// The scoping helper must catch a leak and must pass a correct route. Both are proven against a small route that
/// loads a row and compares its workspace, the pattern every id endpoint follows (WP08 onward).
/// </summary>
public sealed class ScopingAssertionsTests
{
    private const string RowUrlTemplate = "/api/v1/rows/{0}";

    [Fact]
    public async Task A_correct_id_route_answers_not_found_to_another_workspace_and_the_row_to_its_own()
    {
        using var tokens = new TestTokens();
        var workspaceA = Guid.NewGuid();
        var workspaceB = Guid.NewGuid();
        var row = Guid.NewGuid();
        await using var host = await RowHost.StartAsync(tokens, row, workspaceA, enforceWorkspace: true);

        using var ownAnswer = await Get(host.Client, tokens.Issue(TestTokens.Membership(workspaceA, "Member")), row);
        using var foreignAnswer = await Get(host.Client, tokens.Issue(TestTokens.Membership(workspaceB, "Owner")), row);

        Assert.Equal(HttpStatusCode.OK, ownAnswer.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, foreignAnswer.StatusCode);
        await ScopingAssertions.AssertNotFoundForOtherWorkspaceAsync(
            host.Client, HttpMethod.Get, string.Format(RowUrlTemplate, row),
            tokens.Issue(TestTokens.Membership(workspaceB, "Owner")), TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task The_helper_fails_when_a_route_returns_another_workspaces_row()
    {
        using var tokens = new TestTokens();
        var workspaceA = Guid.NewGuid();
        var workspaceB = Guid.NewGuid();
        var row = Guid.NewGuid();
        // enforceWorkspace: false is the leak the rule forbids: the row is returned to any signed-in caller.
        await using var host = await RowHost.StartAsync(tokens, row, workspaceA, enforceWorkspace: false);

        await Assert.ThrowsAsync<Xunit.Sdk.EqualException>(() => ScopingAssertions.AssertNotFoundForOtherWorkspaceAsync(
            host.Client, HttpMethod.Get, string.Format(RowUrlTemplate, row),
            tokens.Issue(TestTokens.Membership(workspaceB, "Owner")), TestContext.Current.CancellationToken));
    }

    private static Task<HttpResponseMessage> Get(HttpClient client, string token, Guid row)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, string.Format(RowUrlTemplate, row));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client.SendAsync(request, TestContext.Current.CancellationToken);
    }

    /// <summary>A minimal host with one row owned by one workspace, and the id route under test.</summary>
    private sealed class RowHost : IAsyncDisposable
    {
        private WebApplication _app = null!;

        public HttpClient Client { get; private init; } = null!;

        public static async Task<RowHost> StartAsync(TestTokens tokens, Guid row, Guid owner, bool enforceWorkspace)
        {
            var rows = new ConcurrentDictionary<Guid, Guid>();
            rows[row] = owner;

            var builder = WebApplication.CreateBuilder();
            builder.WebHost.UseUrls("http://127.0.0.1:0");
            builder.Configuration["Auth:MetadataAddress"] = "http://localhost/.well-known/openid-configuration";
            builder.Configuration["Auth:Issuer"] = TestTokens.Issuer;
            builder.Configuration["Auth:Audience"] = TestTokens.Audience;
            builder.Configuration["Auth:RequireHttpsMetadata"] = "false";
            builder.Services.AddJwtAuthentication(builder.Configuration);
            tokens.TrustIn(builder.Services);

            var app = builder.Build();
            app.UseAuthentication();
            app.UseAuthorization();

            app.MapGroup("/api/v1").RequireActiveWorkspace().MapGet("/rows/{id:guid}", (Guid id, HttpContext http) =>
            {
                var workspace = WorkspaceClaims.Resolve(http.User).WorkspaceId;
                if (enforceWorkspace && (!rows.TryGetValue(id, out var owned) || owned != workspace))
                {
                    return Results.Problem(
                        title: "Not found.",
                        statusCode: StatusCodes.Status404NotFound,
                        extensions: new Dictionary<string, object?> { ["code"] = "not_found" });
                }

                return rows.ContainsKey(id) ? Results.Ok(new { id }) : Results.NotFound();
            });

            await app.StartAsync();
            return new RowHost
            {
                _app = app,
                Client = new HttpClient { BaseAddress = new Uri(app.Urls.First()) },
            };
        }

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await _app.DisposeAsync();
        }
    }
}
