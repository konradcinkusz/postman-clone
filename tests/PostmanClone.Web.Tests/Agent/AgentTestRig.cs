using Bunit;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using PostmanClone.Web.Agent;

namespace PostmanClone.Web.Tests.Agent;

/// <summary>
/// The agent's circuit services for a bUnit context (WP31). The JS interop runs loose: a call that a test did not set up answers
/// the default, so a probe with no setup reads as "unreachable", which is what a page without an agent sees.
/// </summary>
public static class AgentTestRig
{
    public static void AddAgentServices(BunitContext context)
    {
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        context.Services.TryAddSingleton(TimeProvider.System);
        context.Services.AddSingleton(AgentWebOptions.From(new ConfigurationBuilder().Build()));
        context.Services.AddSingleton<AgentPairingTickets>();
        context.Services.AddScoped<AgentBridge>();
        context.Services.AddScoped<AgentConnection>();
    }
}
