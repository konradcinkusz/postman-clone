using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using PostmanClone.ServiceDefaults.Integrations;
using Xunit;

namespace PostmanClone.ServiceDefaults.Tests.Health;

/// <summary>CONTRACTS §0 health shape: <c>status</c> plus one flat key per optional integration (P8).</summary>
public sealed class HealthEndpointTests
{
    [Fact]
    public async Task Health_reports_status_and_each_optional_integration_as_a_flat_key_and_stays_200()
    {
        await using var app = await TestHost.StartAsync(builder =>
            builder.Services.AddOptionalIntegration<UnavailableSecrets>());
        using var client = TestHost.ClientFor(app);

        using var response = await client.GetAsync("/health", TestContext.Current.CancellationToken);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(["status", "secrets"], body.RootElement.EnumerateObject().Select(property => property.Name));
        Assert.Equal("healthy", body.RootElement.GetProperty("status").GetString());
        Assert.Equal("unavailable", body.RootElement.GetProperty("secrets").GetString());
    }

    [Fact]
    public async Task Alive_is_liveness_only_and_lists_no_integrations()
    {
        await using var app = await TestHost.StartAsync(builder =>
            builder.Services.AddOptionalIntegration<UnavailableSecrets>());
        using var client = TestHost.ClientFor(app);

        using var response = await client.GetAsync("/alive", TestContext.Current.CancellationToken);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(["status"], body.RootElement.EnumerateObject().Select(property => property.Name));
    }

    [Fact]
    public async Task A_failing_required_check_answers_health_503_and_leaves_liveness_alone()
    {
        await using var app = await TestHost.StartAsync(builder =>
            builder.Services.AddHealthChecks().AddCheck("required", () => HealthCheckResult.Unhealthy("down")));
        using var client = TestHost.ClientFor(app);

        using var readiness = await client.GetAsync("/health", TestContext.Current.CancellationToken);
        using var liveness = await client.GetAsync("/alive", TestContext.Current.CancellationToken);
        using var body = JsonDocument.Parse(await readiness.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

        Assert.Equal(System.Net.HttpStatusCode.ServiceUnavailable, readiness.StatusCode);
        Assert.Equal("unhealthy", body.RootElement.GetProperty("status").GetString());
        Assert.Equal(System.Net.HttpStatusCode.OK, liveness.StatusCode);
    }

    [Fact]
    public async Task Health_is_healthy_with_no_optional_integrations_registered()
    {
        await using var app = await TestHost.StartAsync();
        using var client = TestHost.ClientFor(app);

        using var response = await client.GetAsync("/health", TestContext.Current.CancellationToken);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(["status"], body.RootElement.EnumerateObject().Select(property => property.Name));
    }

    public sealed class UnavailableSecrets : IOptionalIntegration
    {
        public string Name => "secrets";

        public string Status => OptionalIntegrationStatus.Unavailable;
    }
}
