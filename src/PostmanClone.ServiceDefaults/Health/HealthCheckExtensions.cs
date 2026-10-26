using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using PostmanClone.ServiceDefaults.Integrations;

namespace PostmanClone.ServiceDefaults.Health;

public static class HealthCheckExtensions
{
    public static TBuilder AddDefaultHealthChecks<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        builder.Services.AddHealthChecks()
            .AddCheck(LivenessCheckName, () => HealthCheckResult.Healthy(), [HealthEndpoints.LiveTag])
            .AddCheck<OptionalIntegrationHealthCheck>(OptionalIntegrationHealthCheck.CheckName);
        return builder;
    }

    public const string LivenessCheckName = "self";
}
