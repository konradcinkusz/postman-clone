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
        Assert.Equal("no-referrer", response.Headers.GetValues("Referrer-Policy").Single());
        Assert.Equal("DENY", response.Headers.GetValues("X-Frame-Options").Single());
    }

    private static void MapBrowserEndpoints(WebApplication app)
    {
        app.MapGet("/", () => Results.Ok()).AllowAnonymous();
        app.MapPost("/_blazor/negotiate", () => Results.Ok()).AllowAnonymous();
        app.MapGet("/_blazor", () => Results.Ok()).AllowAnonymous();
    }
}
