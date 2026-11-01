using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using PostmanClone.Web.Agent;
using PostmanClone.Web.Tests.Host;

namespace PostmanClone.Web.Tests.Agent;

/// <summary>
/// The agent's host rules, read from the running Web (WP31, ADR-0028): the CSP's connect-src names exactly the two loopback origins,
/// the run route refuses an anonymous caller and a foreign origin, and the form posts take no action without a session.
/// </summary>
public sealed class AgentHostTests(WebHostFactory factory) : IClassFixture<WebHostFactory>
{
    private static CancellationToken Ct => Xunit.TestContext.Current.CancellationToken;

    /// <summary>A client that does not follow redirects, so a sign-in redirect is seen as the redirect it is.</summary>
    private static HttpClient NoRedirects(WebHostFactory factory) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    private static IReadOnlyDictionary<string, string[]> Directives(string policy) =>
        policy.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(directive => directive.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            .ToDictionary(parts => parts[0], parts => parts[1..]);

    [Fact]
    public async Task The_connect_src_names_exactly_the_two_loopback_origins_and_nothing_else()
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/about", Ct);

        var policy = Directives(response.Headers.GetValues("Content-Security-Policy").Single());
        Assert.Equal(["'self'", "http://127.0.0.1:47631", "http://localhost:47631"], policy["connect-src"]);
        Assert.DoesNotContain(policy["connect-src"], origin => origin.Contains('*', StringComparison.Ordinal));
    }

    [Fact]
    public async Task The_script_source_stays_self_so_the_only_script_the_page_adds_is_the_Webs_own()
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/about", Ct);

        var policy = Directives(response.Headers.GetValues("Content-Security-Policy").Single());
        Assert.Equal(["'self'"], policy["script-src"]);
        Assert.Equal(["'self'"], policy["form-action"]);
    }

    [Fact]
    public async Task The_run_route_refuses_an_anonymous_caller_with_401_and_no_redirect()
    {
        using var client = NoRedirects(factory);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/agent/runs") { Content = JsonContent.Create(new { source = "agent" }) };
        request.Headers.Add("Origin", "http://localhost");

        using var response = await client.SendAsync(request, Ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task The_run_route_refuses_a_foreign_origin_before_it_reads_the_body()
    {
        using var client = NoRedirects(factory);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/agent/runs") { Content = JsonContent.Create(new { source = "agent" }) };
        request.Headers.Add("Origin", "https://evil.example");

        using var response = await client.SendAsync(request, Ct);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Theory]
    [InlineData("/agent/pairing-codes")]
    [InlineData("/agent/installations/00000000-0000-0000-0000-000000000001/revoke")]
    public async Task The_agent_form_posts_send_an_anonymous_caller_to_sign_in_and_carry_no_ticket(string path)
    {
        using var client = NoRedirects(factory);
        using var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = new FormUrlEncodedContent([]) };
        request.Headers.Add("Origin", "http://localhost");

        using var response = await client.SendAsync(request, Ct);

        // Anonymous is sent to sign-in, never to the agent page with a code: the redirect is not the agent page and carries no ticket.
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var location = response.Headers.Location?.ToString() ?? string.Empty;
        Assert.DoesNotContain("ticket=", location, StringComparison.Ordinal);
        Assert.DoesNotContain("/agent", location, StringComparison.Ordinal);
    }
}
