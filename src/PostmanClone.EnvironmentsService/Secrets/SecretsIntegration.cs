using PostmanClone.ServiceDefaults.Integrations;

namespace PostmanClone.EnvironmentsService.Secrets;

/// <summary>Reports the secret protection state on <c>/health</c> as <c>"secrets": "available" | "unavailable"</c> (P8).</summary>
public sealed class SecretsIntegration(ISecretProtector protector) : IOptionalIntegration
{
    public string Name => "secrets";

    public string Status => protector.IsAvailable ? OptionalIntegrationStatus.Available : OptionalIntegrationStatus.Unavailable;
}
