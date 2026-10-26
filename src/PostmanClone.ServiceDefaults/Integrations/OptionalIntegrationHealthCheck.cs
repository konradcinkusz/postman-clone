using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace PostmanClone.ServiceDefaults.Integrations;

/// <summary>
/// The optional-integration status registry: every registered <see cref="IOptionalIntegration"/> is listed in
/// the check's data. It is always healthy, because a missing optional integration is a feature gap, not an outage.
/// </summary>
public sealed class OptionalIntegrationHealthCheck(IEnumerable<IOptionalIntegration> integrations) : IHealthCheck
{
    public const string CheckName = "optional-integrations";

    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var data = new Dictionary<string, object>();
        foreach (var integration in integrations)
        {
            data[integration.Name] = integration.Status;
        }

        return Task.FromResult(HealthCheckResult.Healthy("Optional integrations are reported, not required.", data));
    }
}
