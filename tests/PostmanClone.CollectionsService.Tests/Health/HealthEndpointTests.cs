using System.Net;
using PostmanClone.CollectionsService.Tests.Support;
using PostmanClone.TestKit;
using Xunit;

namespace PostmanClone.CollectionsService.Tests.Health;

/// <summary>Readiness and liveness are anonymous (CONTRACTS §0); the domain routes are not.</summary>
public sealed class HealthEndpointTests
{
    [Fact]
    public async Task Health_and_alive_answer_without_a_token_once_the_schema_is_applied()
    {
        using var tokens = new TestTokens();
        await using var factory = new CollectionsFactory(tokens);
        await SchemaWait.AppliedAsync(factory, TestContext.Current.CancellationToken);
        using var client = factory.CreateClient();

        using var health = await client.GetAsync("/health", TestContext.Current.CancellationToken);
        using var alive = await client.GetAsync("/alive", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, health.StatusCode);
        Assert.Contains("\"status\":\"healthy\"", await health.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Equal(HttpStatusCode.OK, alive.StatusCode);
    }

    [Fact]
    public async Task A_domain_route_without_a_token_is_unauthorized()
    {
        using var tokens = new TestTokens();
        await using var factory = new CollectionsFactory(tokens);
        await SchemaWait.AppliedAsync(factory, TestContext.Current.CancellationToken);
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/api/v1/internal/workspace", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
