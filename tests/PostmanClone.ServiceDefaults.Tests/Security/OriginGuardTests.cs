using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using PostmanClone.TestKit;
using Xunit;

namespace PostmanClone.ServiceDefaults.Tests.Security;

/// <summary>
/// The origin guard and security headers (BLAZOR-WEB-APP §8, CONTRACTS §0). The browser paths are the three that
/// matter for a circuit and a form post, plus the one allowed navigation.
/// </summary>
public sealed class OriginGuardTests
{
    private const string ForeignOrigin = "https://attacker.example";
    private const string ConfiguredOrigin = "https://app.example";

    [Fact]
    public async Task A_foreign_origin_cannot_negotiate_a_circuit()
    {
        await using var app = await SecureTestHost.StartAsync(map: MapBrowserEndpoints);
        using var client = TestHost.ClientFor(app);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/_blazor/negotiate");
        request.Headers.TryAddWithoutValidation("Origin", ForeignOrigin);

        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task A_foreign_origin_is_refused_before_the_websocket_upgrade()
    {
        await using var app = await SecureTestHost.StartAsync(map: MapBrowserEndpoints);
        using var client = TestHost.ClientFor(app);
        using var request = new HttpRequestMessage(HttpMethod.Get, "/_blazor");
        request.Headers.TryAddWithoutValidation("Origin", ForeignOrigin);

        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task A_cross_site_request_without_an_origin_is_refused()
    {
        await using var app = await SecureTestHost.StartAsync(map: MapBrowserEndpoints);
        using var client = TestHost.ClientFor(app);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/_blazor/negotiate");
        request.Headers.TryAddWithoutValidation("Sec-Fetch-Site", "cross-site");

        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task A_link_followed_from_another_site_loads_the_page()
    {
        await using var app = await SecureTestHost.StartAsync(map: MapBrowserEndpoints);
        using var client = TestHost.ClientFor(app);
        using var request = new HttpRequestMessage(HttpMethod.Get, "/");
        request.Headers.TryAddWithoutValidation("Sec-Fetch-Site", "cross-site");
        request.Headers.TryAddWithoutValidation("Sec-Fetch-Mode", "navigate");
        request.Headers.TryAddWithoutValidation("Sec-Fetch-Dest", "document");

        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task The_own_origin_and_a_configured_origin_are_allowed_and_a_request_with_no_origin_is_too()
    {
        await using var app = await SecureTestHost.StartAsync(
            configure: builder => builder.Configuration["AllowedOrigins:0"] = ConfiguredOrigin,
            map: MapBrowserEndpoints);
        using var client = TestHost.ClientFor(app);
        var ownOrigin = app.Urls.First();

        using var own = new HttpRequestMessage(HttpMethod.Post, "/_blazor/negotiate");
        own.Headers.TryAddWithoutValidation("Origin", ownOrigin);
        using var configured = new HttpRequestMessage(HttpMethod.Post, "/_blazor/negotiate");
        configured.Headers.TryAddWithoutValidation("Origin", ConfiguredOrigin);
        using var none = new HttpRequestMessage(HttpMethod.Post, "/_blazor/negotiate");

        using var ownResponse = await client.SendAsync(own, TestContext.Current.CancellationToken);
        using var configuredResponse = await client.SendAsync(configured, TestContext.Current.CancellationToken);
        using var noneResponse = await client.SendAsync(none, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, ownResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, configuredResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, noneResponse.StatusCode);
    }

    [Fact]
    public async Task Every_response_carries_the_security_headers()
    {
        await using var app = await SecureTestHost.StartAsync(map: MapBrowserEndpoints);
        using var client = TestHost.ClientFor(app);

        using var response = await client.GetAsync("/", TestContext.Current.CancellationToken);

        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("same-origin", response.Headers.GetValues("Referrer-Policy").Single());
        Assert.Equal("DENY", response.Headers.GetValues("X-Frame-Options").Single());
    }

    [Fact]
    public async Task A_form_post_from_the_own_origin_is_allowed_as_a_browser_sends_it()
    {
        // The sign-in form (ADR-0022): a same-origin POST carries the real Origin and Referer and Sec-Fetch-Site same-origin.
        await using var app = await SecureTestHost.StartAsync(map: MapBrowserEndpoints);
        using var client = TestHost.ClientFor(app);
        var ownOrigin = app.Urls.First();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/_blazor/negotiate")
        {
            Content = new StringContent("email=a%40example.test&password=x", System.Text.Encoding.UTF8, "application/x-www-form-urlencoded"),
        };
        request.Headers.TryAddWithoutValidation("Origin", ownOrigin);
        request.Headers.TryAddWithoutValidation("Referer", ownOrigin + "/sign-in");
        request.Headers.TryAddWithoutValidation("Sec-Fetch-Site", "same-origin");
        request.Headers.TryAddWithoutValidation("Sec-Fetch-Mode", "navigate");
        request.Headers.TryAddWithoutValidation("Sec-Fetch-Dest", "document");

        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task An_unsafe_request_with_the_null_origin_is_refused()
    {
        // What a browser sends when the policy is no-referrer. The guard must not accept it, whatever the policy is.
        await using var app = await SecureTestHost.StartAsync(map: MapBrowserEndpoints);
        using var client = TestHost.ClientFor(app);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/_blazor/negotiate");
        request.Headers.TryAddWithoutValidation("Origin", "null");

        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task An_unsafe_request_with_a_foreign_referer_is_refused_even_without_an_origin()
    {
        await using var app = await SecureTestHost.StartAsync(map: MapBrowserEndpoints);
        using var client = TestHost.ClientFor(app);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/_blazor/negotiate");
        request.Headers.TryAddWithoutValidation("Referer", ForeignOrigin + "/page");

        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task An_unsafe_request_with_an_unreadable_referer_is_refused()
    {
        await using var app = await SecureTestHost.StartAsync(map: MapBrowserEndpoints);
        using var client = TestHost.ClientFor(app);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/_blazor/negotiate");
        request.Headers.TryAddWithoutValidation("Referer", "not a url");

        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task An_unsafe_request_with_a_configured_or_own_referer_is_allowed()
    {
        await using var app = await SecureTestHost.StartAsync(
            configure: builder => builder.Configuration["AllowedOrigins:0"] = ConfiguredOrigin,
            map: MapBrowserEndpoints);
        using var client = TestHost.ClientFor(app);
        var ownOrigin = app.Urls.First();

        using var own = new HttpRequestMessage(HttpMethod.Post, "/_blazor/negotiate");
        own.Headers.TryAddWithoutValidation("Referer", ownOrigin + "/sign-in");
        using var configured = new HttpRequestMessage(HttpMethod.Post, "/_blazor/negotiate");
        configured.Headers.TryAddWithoutValidation("Referer", ConfiguredOrigin + "/page");

        using var ownResponse = await client.SendAsync(own, TestContext.Current.CancellationToken);
        using var configuredResponse = await client.SendAsync(configured, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, ownResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, configuredResponse.StatusCode);
    }

    [Fact]
    public async Task A_safe_request_with_a_foreign_referer_is_not_refused_by_the_referer_rule()
    {
        // The Referer rule covers unsafe methods only; a page load from another site stays allowed (the top-level rule above).
        await using var app = await SecureTestHost.StartAsync(map: MapBrowserEndpoints);
        using var client = TestHost.ClientFor(app);
        using var request = new HttpRequestMessage(HttpMethod.Get, "/");
        request.Headers.TryAddWithoutValidation("Referer", ForeignOrigin + "/page");

        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private static void MapBrowserEndpoints(WebApplication app)
    {
        app.MapGet("/", () => Results.Ok()).AllowAnonymous();
        app.MapPost("/_blazor/negotiate", () => Results.Ok()).AllowAnonymous();
        app.MapGet("/_blazor", () => Results.Ok()).AllowAnonymous();
    }
}
