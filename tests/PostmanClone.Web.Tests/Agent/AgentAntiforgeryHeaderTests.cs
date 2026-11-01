using Microsoft.AspNetCore.Antiforgery;
using PostmanClone.Web.Agent;

namespace PostmanClone.Web.Tests.Agent;

/// <summary>
/// The run record's antiforgery header is the one the framework validates (WP32). WP31 sent X-CSRF-TOKEN while the framework reads
/// RequestVerificationToken, so every run record was refused with 400 and no send reached the history. The browser journeys caught it;
/// this test keeps the constant and the framework's default in step.
/// </summary>
public sealed class AgentAntiforgeryHeaderTests
{
    [Fact]
    public void The_run_record_header_is_the_name_the_framework_validates()
    {
        Assert.Equal(AgentEndpoints.AntiforgeryHeader, new AntiforgeryOptions().HeaderName);
    }
}
