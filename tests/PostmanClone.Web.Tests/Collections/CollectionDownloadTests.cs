using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using PostmanClone.ServiceDefaults.Authentication;
using PostmanClone.Web.Auth;
using PostmanClone.Web.Tests.Auth;

namespace PostmanClone.Web.Tests.Collections;

/// <summary>
/// The BFF download of a collection (WP23, <c>GET /download/collections/{id}</c>) on the real Web pipeline: the fake authservice
/// signs the user in, the fake CollectionsService answers the export. Each test is one request a browser would make. The
/// assertions cover the status, the headers the download needs, the bearer the server attached, and what was logged.
/// </summary>
public sealed class CollectionDownloadTests : IClassFixture<AuthHostFactory>
{
    private const string SessionCookie = "pc.session";

    private readonly AuthHostFactory _factory;
    private readonly FakeAuthService _auth;

    public CollectionDownloadTests(AuthHostFactory factory)
    {
        _factory = factory;
        _auth = factory.AuthService;
        _auth.Reset();
        factory.Collections.Reset();
    }

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private FakeCollectionsService Fake => _factory.Collections;

    private HttpClient Browser() => _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false, AllowAutoRedirect = false });

    /// <summary>Signs the known address in and returns its session cookie as <c>name=value</c>.</summary>
    private async Task<string> SignInAsync(HttpClient client)
    {
        var (cookie, token) = await SignInForms.AntiforgeryAsync(client, "/sign-in", sessionCookie: null, Cancellation);
        var fields = new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["email"] = FakeAuthService.KnownEmail,
            ["password"] = FakeAuthService.KnownPassword,
            ["returnUrl"] = string.Empty,
        };
        using var response = await client.SendAsync(SignInForms.Post("/sign-in", fields, cookie), Cancellation);
        var setCookie = SignInForms.SetCookie(response, SessionCookie);
        Assert.NotNull(setCookie);
        return $"{SessionCookie}={SignInForms.ValueOf(setCookie)}";
    }

    private async Task<HttpResponseMessage> DownloadAsync(HttpClient client, string path, string? session) =>
        await client.SendAsync(SignInForms.Get(path, session), Cancellation);

    private static string LocationOf(HttpResponseMessage response) => response.Headers.Location?.OriginalString ?? string.Empty;

    [Fact]
    public async Task An_anonymous_download_is_sent_to_sign_in_and_nothing_is_fetched()
    {
        var collection = Fake.AddCollection("Billing");
        using var client = Browser();

        using var response = await DownloadAsync(client, $"/download/collections/{collection}", session: null);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.StartsWith("/sign-in", LocationOf(response));
        Assert.Empty(Fake.Calls);
    }

    [Fact]
    public async Task A_member_downloads_the_collection_as_an_attachment_that_is_not_cached()
    {
        _auth.Join(Guid.NewGuid(), "Acme", WorkspaceRoles.Member);
        var collection = Fake.AddCollection("Billing");
        using var client = Browser();
        var session = await SignInAsync(client);

        using var response = await DownloadAsync(client, $"/download/collections/{collection}", session);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("attachment", response.Content.Headers.ContentDisposition?.DispositionType);
        Assert.Equal("Billing.postman_collection.json", response.Content.Headers.ContentDisposition?.FileName?.Trim('"'));
        Assert.Contains("no-store", response.Headers.CacheControl?.ToString() ?? string.Empty);
        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal(Fake.ExportBody, await response.Content.ReadAsStringAsync(Cancellation));
    }

    [Fact]
    public async Task The_server_attaches_the_session_bearer_and_the_browser_never_sees_it()
    {
        _auth.Join(Guid.NewGuid(), "Acme", WorkspaceRoles.Member);
        var collection = Fake.AddCollection("Billing");
        using var client = Browser();
        var session = await SignInAsync(client);

        using var response = await DownloadAsync(client, $"/download/collections/{collection}", session);

        // The service saw the token the sign-in issued; the response to the browser carries no authorization at all.
        Assert.Equal(_auth.LastAccessToken, Fake.LastBearer);
        Assert.False(response.Headers.Contains("Authorization"));
        Assert.DoesNotContain(_auth.LastAccessToken!, await response.Content.ReadAsStringAsync(Cancellation), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Scoping_GET_download_collections_id()
    {
        // The fake holds one workspace. An id it does not hold stands for an id that belongs to another workspace, or to none.
        _auth.Join(Guid.NewGuid(), "Acme", WorkspaceRoles.Member);
        Fake.AddCollection("Billing");
        var foreign = Guid.NewGuid();
        using var client = Browser();
        var session = await SignInAsync(client);

        using var response = await DownloadAsync(client, $"/download/collections/{foreign}", session);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Contains($"GET /api/v1/collections/{foreign}/export/postman", Fake.Calls);
        Assert.DoesNotContain(Fake.ExportBody, await response.Content.ReadAsStringAsync(Cancellation), StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_id_that_is_not_a_guid_is_not_found_and_nothing_is_fetched()
    {
        _auth.Join(Guid.NewGuid(), "Acme", WorkspaceRoles.Member);
        using var client = Browser();
        var session = await SignInAsync(client);

        using var response = await DownloadAsync(client, "/download/collections/not-a-guid", session);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Empty(Fake.Calls);
    }

    [Fact]
    public async Task A_user_with_two_workspaces_is_sent_to_the_explanation_page_and_nothing_is_fetched()
    {
        _auth.Join(Guid.NewGuid(), "Acme", WorkspaceRoles.Member);
        _auth.Join(Guid.NewGuid(), "Globex", WorkspaceRoles.Member);
        var collection = Fake.AddCollection("Billing");
        using var client = Browser();
        var session = await SignInAsync(client);

        using var response = await DownloadAsync(client, $"/download/collections/{collection}", session);

        Assert.Equal(WorkspaceRouting.NoWorkspacePath, LocationOf(response));
        Assert.Empty(Fake.Calls);
    }

    [Fact]
    public async Task An_unavailable_collections_service_is_a_503_that_names_no_collection()
    {
        _auth.Join(Guid.NewGuid(), "Acme", WorkspaceRoles.Member);
        var collection = Fake.AddCollection("Billing");
        Fake.Unavailable = true;
        using var client = Browser();
        var session = await SignInAsync(client);

        using var response = await DownloadAsync(client, $"/download/collections/{collection}", session);
        var body = await response.Content.ReadAsStringAsync(Cancellation);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Contains("did not answer", body);
        Assert.DoesNotContain("Billing", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_export_is_never_written_to_the_log_and_the_status_is()
    {
        _auth.Join(Guid.NewGuid(), "Acme", WorkspaceRoles.Member);
        const string marker = "export-body-marker-7c1f";
        Fake.ExportBody = $"{{\"marker\":\"{marker}\"}}";
        var collection = Fake.AddCollection("Billing");
        using var client = Browser();
        var session = await SignInAsync(client);

        using var response = await DownloadAsync(client, $"/download/collections/{collection}", session);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        _ = await response.Content.ReadAsStringAsync(Cancellation);

        var lines = _factory.Logs.Lines.ToArray();
        Assert.DoesNotContain(lines, line => line.Contains(marker, StringComparison.Ordinal));
        // The positive control: the endpoint did log, so the empty result above is not a logger that never ran.
        Assert.Contains(lines, line => line.Contains("Export of collection", StringComparison.Ordinal) && line.Contains("200", StringComparison.Ordinal));
    }
}
