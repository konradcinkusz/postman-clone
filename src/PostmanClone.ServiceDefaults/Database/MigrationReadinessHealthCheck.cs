using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace PostmanClone.ServiceDefaults.Database;

/// <summary>Required check: the schema is applied. Until then the service is not ready to serve requests.</summary>
public sealed class MigrationReadinessHealthCheck(MigrationCompletionSignal signal) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        if (signal.Succeeded)
        {
            return Task.FromResult(HealthCheckResult.Healthy());
        }

        return Task.FromResult(signal.Finished
            ? HealthCheckResult.Unhealthy("The database migration failed.")
            : HealthCheckResult.Unhealthy("The database migration has not finished."));
    }
}
