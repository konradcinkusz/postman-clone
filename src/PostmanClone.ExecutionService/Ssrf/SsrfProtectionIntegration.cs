using Microsoft.Extensions.Options;
using PostmanClone.ExecutionService.Options;
using PostmanClone.ServiceDefaults.Integrations;

namespace PostmanClone.ExecutionService.Ssrf;

/// <summary>
/// Reports whether the sender's address checks are fully on (P8). It reads like an optional integration on
/// <c>/health</c> and in the startup banner, so a relaxed setting is visible without reading configuration.
/// </summary>
public sealed class SsrfProtectionIntegration(IOptions<ExecutionOptions> options) : IOptionalIntegration
{
    public const string StatusEnforced = "enforced";
    public const string StatusRelaxed = "loopback-allowed (non-production)";

    public string Name => "ssrfProtection";

    public string Status => options.Value.Ssrf.Relaxed ? StatusRelaxed : StatusEnforced;
}
