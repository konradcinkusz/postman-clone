using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace PostmanClone.ExecutionService.Tests.Support;

/// <summary>
/// The configuration a sender test starts with. The loopback flag is set here and only here, and only for tests (ADR-0011):
/// the default configuration keeps every loopback target refused.
/// </summary>
public static class SenderSettings
{
    /// <summary>The loopback flag on, and EnvironmentsService pointed at a stand-in.</summary>
    public static Dictionary<string, string> Loopback(Uri environments) => new(StringComparer.Ordinal)
    {
        ["Execution:Ssrf:AllowLoopback"] = "true",
        ["Environments:BaseAddress"] = environments.AbsoluteUri,
    };

    /// <summary>The shipped defaults, with EnvironmentsService pointed at a stand-in.</summary>
    public static Dictionary<string, string> Defaults(Uri environments) => new(StringComparer.Ordinal)
    {
        ["Environments:BaseAddress"] = environments.AbsoluteUri,
    };

    /// <summary>Routes the service's log output to <paramref name="logs"/>, so a test can read what was written.</summary>
    public static Action<IServiceCollection>? WithLogs(CapturingLogs? logs) =>
        logs is null ? null : services => services.AddSingleton<ILoggerProvider>(logs);
}
