using System.Net;

namespace PostmanClone.Web.Tests.Host;

/// <summary>
/// The origin guard on the real Web pipeline (BLAZOR-WEB-APP §8): the three refusals that matter for a circuit and a
/// form post, and the allowed navigation. The guard itself is tested in the kernel; these prove the Web wires it first.
/// </summary>
public sealed class OriginGuardHostTests(WebHostFactory factory) : IClassFixture<WebHostFactory>
{
    private const string ForeignOrigin = "https://attacker.example";

    [Fact]
    public async Task A_foreign_origin_cannot_negotiate_a_circuit()
    {
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/_blazor/negotiate");
        request.Headers.TryAddWithoutValidation("Origin", ForeignOrigin);

        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task A_foreign_origin_is_refused_before_the_websocket_upgrade()
    {
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/_blazor");
        request.Headers.TryAddWithoutValidation("Origin", ForeignOrigin);

        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task A_cross_site_post_without_an_origin_is_refused()
    {
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/_blazor/negotiate");
        request.Headers.TryAddWithoutValidation("Sec-Fetch-Site", "cross-site");

        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task A_link_followed_from_another_site_loads_the_page()
    {
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/sign-in");
        request.Headers.TryAddWithoutValidation("Sec-Fetch-Site", "cross-site");
        request.Headers.TryAddWithoutValidation("Sec-Fetch-Mode", "navigate");
        request.Headers.TryAddWithoutValidation("Sec-Fetch-Dest", "document");

        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task A_form_post_with_the_null_origin_is_refused_and_the_same_origin_form_post_is_not()
    {
        // The sign-in form (ADR-0021): the browser's real Origin passes, the "null" origin of a no-referrer page does not.
        using var client = factory.CreateClient();
        var own = client.BaseAddress!.GetLeftPart(UriPartial.Authority);

        using var nullOrigin = new HttpRequestMessage(HttpMethod.Post, "/sign-in");
        nullOrigin.Headers.TryAddWithoutValidation("Origin", "null");
        using var sameOrigin = new HttpRequestMessage(HttpMethod.Post, "/sign-in");
        sameOrigin.Headers.TryAddWithoutValidation("Origin", own);
        sameOrigin.Headers.TryAddWithoutValidation("Referer", own + "/sign-in");
        sameOrigin.Headers.TryAddWithoutValidation("Sec-Fetch-Site", "same-origin");

        using var nullResponse = await client.SendAsync(nullOrigin, TestContext.Current.CancellationToken);
        using var sameResponse = await client.SendAsync(sameOrigin, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, nullResponse.StatusCode);
        Assert.NotEqual(HttpStatusCode.Forbidden, sameResponse.StatusCode);
    }

    [Fact]
    public async Task The_web_sends_same_origin_as_its_referrer_policy()
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/sign-in", TestContext.Current.CancellationToken);

        Assert.Equal("same-origin", response.Headers.GetValues("Referrer-Policy").Single());
    }

    [Fact]
    public async Task The_own_origin_is_allowed_to_negotiate_a_circuit()
    {
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/_blazor/negotiate");
        request.Headers.TryAddWithoutValidation("Origin", client.BaseAddress!.GetLeftPart(UriPartial.Authority));

        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.NotEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
