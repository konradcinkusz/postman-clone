using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Options;
using PostmanClone.ExecutionService.Options;
using PostmanClone.ExecutionService.Ssrf;
using PostmanClone.ExecutionService.Tests.Support;
using Xunit;

namespace PostmanClone.ExecutionService.Tests.Health;

/// <summary>Liveness and readiness (CONTRACTS §0), and the ssrfProtection entry (WP06, P8).</summary>
public sealed class HealthEndpointTests
{
    [Fact]
    public async Task Liveness_and_readiness_answer_anonymously()
    {
        using var factory = new ExecutionServiceFactory();
        using var client = await ServiceClient.ReadyAsync(factory, TestContext.Current.CancellationToken);

        using var alive = await client.GetAsync("/alive", TestContext.Current.CancellationToken);
        using var health = await client.GetAsync("/health", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, alive.StatusCode);
        Assert.Equal(HttpStatusCode.OK, health.StatusCode);
    }

    [Fact]
    public async Task Readiness_reports_SSRF_protection_as_enforced_by_default()
    {
        using var factory = new ExecutionServiceFactory("Production");
        using var client = await ServiceClient.ReadyAsync(factory, TestContext.Current.CancellationToken);

        var payload = await HealthPayloadAsync(client);

        Assert.Equal("healthy", payload.GetProperty("status").GetString());
        Assert.Equal(SsrfProtectionIntegration.StatusEnforced, payload.GetProperty("ssrfProtection").GetString());
    }

    [Fact]
    public async Task Readiness_reports_the_loopback_relaxation_outside_Production()
    {
        using var factory = new ExecutionServiceFactory("Development", new Dictionary<string, string>
        {
            ["Execution:Ssrf:AllowLoopback"] = "true",
        });
        using var client = await ServiceClient.ReadyAsync(factory, TestContext.Current.CancellationToken);

        var payload = await HealthPayloadAsync(client);

        Assert.Equal("loopback-allowed (non-production)", payload.GetProperty("ssrfProtection").GetString());
    }

    [Fact]
    public void The_integration_reads_its_status_from_the_bound_options()
    {
        var options = new ExecutionOptions();
        options.Ssrf.AllowPrivateRanges = true;

        var integration = new SsrfProtectionIntegration(global::Microsoft.Extensions.Options.Options.Create(options));

        Assert.Equal("ssrfProtection", integration.Name);
        Assert.Equal(SsrfProtectionIntegration.StatusRelaxed, integration.Status);
    }

    private static async Task<JsonElement> HealthPayloadAsync(HttpClient client)
    {
        using var response = await client.GetAsync("/health", TestContext.Current.CancellationToken);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        return document.RootElement.Clone();
    }
}
