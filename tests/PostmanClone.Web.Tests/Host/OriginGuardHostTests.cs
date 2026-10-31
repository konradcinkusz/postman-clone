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
    public async Task The_own_origin_is_allowed_to_negotiate_a_circuit()
    {
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/_blazor/negotiate");
        request.Headers.TryAddWithoutValidation("Origin", client.BaseAddress!.GetLeftPart(UriPartial.Authority));

        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.NotEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
