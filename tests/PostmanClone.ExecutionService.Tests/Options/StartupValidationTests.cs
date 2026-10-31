using Microsoft.Extensions.Options;
using PostmanClone.ExecutionService.Tests.Support;
using Xunit;

namespace PostmanClone.ExecutionService.Tests.Options;

/// <summary>The host refuses to start with an SSRF relaxation outside Development, and the message names the setting.</summary>
public sealed class StartupValidationTests
{
    [Fact]
    public void Production_with_AllowLoopback_fails_to_start_and_names_the_setting()
    {
        using var factory = new ExecutionServiceFactory("Production", new Dictionary<string, string>
        {
            ["Execution:Ssrf:AllowLoopback"] = "true",
        });

        var exception = Assert.ThrowsAny<Exception>(() => factory.CreateClient());

        var validation = Assert.IsType<OptionsValidationException>(FindInChain<OptionsValidationException>(exception));
        Assert.Contains("Execution:Ssrf:AllowLoopback must be false outside Development.", validation.Failures);
    }

    [Fact]
    public void Production_with_AllowPrivateRanges_fails_to_start_and_names_the_setting()
    {
        using var factory = new ExecutionServiceFactory("Production", new Dictionary<string, string>
        {
            ["Execution:Ssrf:AllowPrivateRanges"] = "true",
        });

        var exception = Assert.ThrowsAny<Exception>(() => factory.CreateClient());

        var validation = Assert.IsType<OptionsValidationException>(FindInChain<OptionsValidationException>(exception));
        Assert.Contains("Execution:Ssrf:AllowPrivateRanges must be false outside Development.", validation.Failures);
    }

    [Fact]
    public void Development_with_AllowLoopback_starts()
    {
        using var factory = new ExecutionServiceFactory("Development", new Dictionary<string, string>
        {
            ["Execution:Ssrf:AllowLoopback"] = "true",
        });

        using var client = factory.CreateClient();

        Assert.NotNull(client);
    }

    private static T? FindInChain<T>(Exception? exception) where T : Exception
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is T match)
            {
                return match;
            }
        }

        return null;
    }
}
