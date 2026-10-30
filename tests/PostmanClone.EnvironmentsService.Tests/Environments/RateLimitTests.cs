using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using PostmanClone.ServiceDefaults.RateLimiting;
using Xunit;
using static PostmanClone.EnvironmentsService.Tests.Environments.EnvironmentsHost;

namespace PostmanClone.EnvironmentsService.Tests.Environments;

/// <summary>
/// <c>resolve</c> is metered by the <c>api</c> policy (CONTRACTS §3, §0). The policy's limit is a fixed one-minute
/// window on the system clock, so a test that counts 301 requests would be timing-dependent. The test checks the
/// endpoint's metadata instead; the policy's own limit is the kernel's to test.
/// </summary>
public sealed class RateLimitTests
{
    [Fact]
    public async Task Resolve_carries_the_api_rate_limit_policy()
    {
        await using var host = await EnvironmentsHost.StartAsync(cancellationToken: TestContext.Current.CancellationToken);
        var endpoints = host.Factory.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>();

        var resolve = endpoints.Single(endpoint => endpoint.RoutePattern.RawText == $"{EnvironmentsRoute}/{{id:guid}}/resolve");
        var policies = resolve.Metadata.GetOrderedMetadata<EnableRateLimitingAttribute>().Select(attribute => attribute.PolicyName).ToArray();

        Assert.Equal(new[] { RateLimitPolicies.Api }, policies);
    }
}
