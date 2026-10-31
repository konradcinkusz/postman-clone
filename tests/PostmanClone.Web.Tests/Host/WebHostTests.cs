using System.Net;
using Microsoft.AspNetCore.Components.Server;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using PostmanClone.Web.Hosting;

namespace PostmanClone.Web.Tests.Host;

/// <summary>The host's own wiring: circuit limits, the health endpoint and the security headers on every response.</summary>
public sealed class WebHostTests(WebHostFactory factory) : IClassFixture<WebHostFactory>
{
    [Fact]
    public void Circuits_keep_a_disconnected_session_for_three_minutes()
    {
        var options = factory.Services.GetRequiredService<IOptions<CircuitOptions>>().Value;

        Assert.Equal(TimeSpan.FromMinutes(3), options.DisconnectedCircuitRetentionPeriod);
    }

    [Fact]
    public void The_circuit_accepts_a_two_megabyte_message()
    {
        var options = factory.Services.GetRequiredService<IOptions<HubOptions>>().Value;

        Assert.Equal(2 * 1024 * 1024, options.MaximumReceiveMessageSize);
        Assert.Equal(WebHostExtensions.MaximumCircuitMessageBytes, options.MaximumReceiveMessageSize);
    }

    [Fact]
    public async Task Health_answers_on_the_web_host()
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/health", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("\"status\"", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Every_response_carries_the_security_headers_and_a_content_security_policy()
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/about", TestContext.Current.CancellationToken);

        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("same-origin", response.Headers.GetValues("Referrer-Policy").Single());
        Assert.Equal("DENY", response.Headers.GetValues("X-Frame-Options").Single());
        // One policy, exactly ours: Blazor's own frame-ancestors header must not sit beside it.
        Assert.Equal(ContentSecurityPolicyMiddleware.Policy, response.Headers.GetValues("Content-Security-Policy").Single());
    }
}
