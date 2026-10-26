using PostmanClone.ServiceDefaults.Integrations;
using PostmanClone.ServiceDefaults.Startup;
using Xunit;

namespace PostmanClone.ServiceDefaults.Tests.Startup;

public sealed class StartupBannerTests
{
    [Fact]
    public void The_banner_lists_each_optional_integration_with_its_status()
    {
        var text = StartupBanner.Format([new Fake("secrets", OptionalIntegrationStatus.Unavailable), new Fake("queue", OptionalIntegrationStatus.Available)]);

        Assert.Equal(string.Join(Environment.NewLine, "Optional integrations:", "  secrets: unavailable", "  queue: available"), text);
    }

    private sealed record Fake(string Name, string Status) : IOptionalIntegration;
}
