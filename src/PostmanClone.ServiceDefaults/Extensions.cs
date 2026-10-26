using Microsoft.AspNetCore.Builder;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.ServiceDiscovery;
using PostmanClone.ServiceDefaults.Health;
using PostmanClone.ServiceDefaults.Problems;
using PostmanClone.ServiceDefaults.Startup;
using PostmanClone.ServiceDefaults.Telemetry;

[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("PostmanClone.ServiceDefaults.Tests")]

namespace PostmanClone.ServiceDefaults;

/// <summary>
/// The kernel's two entry points. Every service calls <see cref="AddServiceDefaults{TBuilder}"/> on its
/// builder and <see cref="MapDefaultEndpoints"/> on its application (architecture-standards P2, P2a).
/// </summary>
public static class Extensions
{
    public static TBuilder AddServiceDefaults<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        builder.ConfigureOpenTelemetry();
        builder.AddDefaultHealthChecks();
        builder.Services.AddProblemDetailsDefaults();
        builder.Services.AddStartupBanner();

        builder.Services.AddServiceDiscovery();
        // The one resilience handler for every HttpClient (P2): services add no second one.
        builder.Services.ConfigureHttpClientDefaults(http =>
        {
            http.AddStandardResilienceHandler();
            http.AddServiceDiscovery();
        });

        return builder;
    }

    /// <summary>Maps <c>/health</c> (readiness, with optional integrations) and <c>/alive</c> (liveness).</summary>
    public static WebApplication MapDefaultEndpoints(this WebApplication app)
    {
        app.MapHealthChecks("/health", HealthEndpoints.ReadinessOptions).AllowAnonymous();
        app.MapHealthChecks("/alive", HealthEndpoints.LivenessOptions).AllowAnonymous();
        return app;
    }
}
