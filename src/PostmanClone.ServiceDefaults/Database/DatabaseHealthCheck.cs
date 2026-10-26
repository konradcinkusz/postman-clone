using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace PostmanClone.ServiceDefaults.Database;

/// <summary>Required check: the database answers. Failing it makes readiness unhealthy.</summary>
public sealed class DatabaseHealthCheck<TContext>(TContext context) : IHealthCheck where TContext : DbContext
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext checkContext, CancellationToken cancellationToken = default)
    {
        return await context.Database.CanConnectAsync(cancellationToken)
            ? HealthCheckResult.Healthy()
            : HealthCheckResult.Unhealthy("The database does not answer.");
    }
}
