using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using PostmanClone.TestKit;
using Xunit;

namespace PostmanClone.ServiceDefaults.Tests.Authentication;

/// <summary>
/// A bearer token is accepted only when it is signed by a trusted key and is within issuer, audience and lifetime
/// (ADR-0004). Anything else is 401 on an endpoint that needs a user.
/// </summary>
public sealed class JwtAuthenticationTests
{
    [Fact]
    public async Task A_valid_token_from_the_trusted_issuer_is_accepted()
    {
        using var tokens = new TestTokens();
        await using var app = await SecureTestHost.StartAsync(tokens, map: MapPrivate);
        using var client = TestHost.ClientFor(app);

        using var response = await GetPrivateAsync(client, tokens.Issue());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task An_expired_token_is_rejected()
    {
        using var tokens = new TestTokens();
        await using var app = await SecureTestHost.StartAsync(tokens, map: MapPrivate);
        using var client = TestHost.ClientFor(app);

        using var response = await GetPrivateAsync(client, tokens.Issue(expiresIn: TimeSpan.FromMinutes(-5)));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task A_token_that_expired_within_the_30_second_clock_skew_is_accepted()
    {
        using var tokens = new TestTokens();
        await using var app = await SecureTestHost.StartAsync(tokens, map: MapPrivate);
        using var client = TestHost.ClientFor(app);

        using var response = await GetPrivateAsync(client, tokens.Issue(expiresIn: TimeSpan.FromSeconds(-10)));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task A_token_from_a_different_issuer_is_rejected()
    {
        using var tokens = new TestTokens();
        await using var app = await SecureTestHost.StartAsync(tokens, map: MapPrivate);
        using var client = TestHost.ClientFor(app);

        using var response = await GetPrivateAsync(client, tokens.Issue(issuer: "https://elsewhere.test"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task A_token_for_a_different_audience_is_rejected()
    {
        using var tokens = new TestTokens();
        await using var app = await SecureTestHost.StartAsync(tokens, map: MapPrivate);
        using var client = TestHost.ClientFor(app);

        using var response = await GetPrivateAsync(client, tokens.Issue(audience: "some-other-api"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task An_unsigned_token_is_rejected()
    {
        using var tokens = new TestTokens();
        await using var app = await SecureTestHost.StartAsync(tokens, map: MapPrivate);
        using var client = TestHost.ClientFor(app);
        var token = tokens.IssueUnsigned();

        using var response = await GetPrivateAsync(client, token);

        // A real unsigned token: three segments, the last one empty.
        Assert.EndsWith(".", token);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task An_HS256_token_forged_with_the_public_key_is_rejected()
    {
        using var tokens = new TestTokens();
        await using var app = await SecureTestHost.StartAsync(tokens, map: MapPrivate);
        using var client = TestHost.ClientFor(app);

        using var response = await GetPrivateAsync(client, tokens.IssueHmacForgery());

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task A_token_signed_by_an_untrusted_key_is_rejected()
    {
        using var trusted = new TestTokens();
        using var untrusted = new TestTokens();
        await using var app = await SecureTestHost.StartAsync(trusted, map: MapPrivate);
        using var client = TestHost.ClientFor(app);

        using var response = await GetPrivateAsync(client, untrusted.Issue());

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task An_endpoint_without_an_attribute_requires_a_token()
    {
        using var tokens = new TestTokens();
        await using var app = await SecureTestHost.StartAsync(tokens, map: MapPrivate);
        using var client = TestHost.ClientFor(app);

        using var response = await client.GetAsync("/private", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Health_and_alive_answer_anonymously()
    {
        using var tokens = new TestTokens();
        await using var app = await SecureTestHost.StartAsync(tokens, map: MapPrivate);
        using var client = TestHost.ClientFor(app);

        using var health = await client.GetAsync("/health", TestContext.Current.CancellationToken);
        using var alive = await client.GetAsync("/alive", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, health.StatusCode);
        Assert.Equal(HttpStatusCode.OK, alive.StatusCode);
    }

    [Fact]
    public async Task The_signing_keys_come_from_the_metadata_address_when_no_key_is_pinned()
    {
        using var tokens = new TestTokens();
        await using var authority = await TestHost.StartAsync(
            configure: null,
            map: app =>
            {
                app.MapGet("/.well-known/openid-configuration", (HttpContext http) =>
                    Results.Text(tokens.Discovery($"{http.Request.Scheme}://{http.Request.Host}/jwks"), "application/json"));
                app.MapGet("/jwks", () => Results.Text(tokens.Jwks(), "application/json"));
            });
        var metadataAddress = new Uri(new Uri(authority.Urls.First()), "/.well-known/openid-configuration").ToString();
        await using var app = await SecureTestHost.StartAsync(trusted: null, map: MapPrivate, metadataAddress: metadataAddress);
        using var client = TestHost.ClientFor(app);

        using var response = await GetPrivateAsync(client, tokens.Issue());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private static void MapPrivate(WebApplication app) => app.MapGet("/private", () => Results.Ok());

    private static Task<HttpResponseMessage> GetPrivateAsync(HttpClient client, string token)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/private")
        {
            Headers = { Authorization = new AuthenticationHeaderValue("Bearer", token) },
        };
        return client.SendAsync(request, TestContext.Current.CancellationToken);
    }
}
