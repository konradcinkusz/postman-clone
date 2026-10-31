using Microsoft.AspNetCore.Builder;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.ServiceDiscovery;
using PostmanClone.ServiceDefaults.Health;
using PostmanClone.ServiceDefaults.Resilience;
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
        builder.Services.AddOptions<HttpClientTimeouts>();
        builder.Services.ConfigureHttpClientDefaults(http =>
        {
            // Captured here, because the defaults run once per named client: the handler reads the client's own limits.
            var client = http.Name ?? string.Empty;
            http.AddStandardResilienceHandler().Configure((options, services) => ApplyLimits(options, client, services));
            http.AddServiceDiscovery();
        });

        return builder;
    }

    /// <summary>
    /// A send or a write is never retried: a retry of a POST sends it again. The timeouts of a client listed in
    /// <see cref="HttpClientTimeouts"/> replace the library's; the circuit breaker samples for at least twice the attempt
    /// timeout, which the library requires.
    /// </summary>
    private static void ApplyLimits(HttpStandardResilienceOptions options, string client, IServiceProvider services)
    {
        options.Retry.DisableForUnsafeHttpMethods();
        if (services.GetRequiredService<IOptions<HttpClientTimeouts>>().Value.Totals.TryGetValue(client, out var total))
        {
            options.AttemptTimeout.Timeout = total;
            options.TotalRequestTimeout.Timeout = total;
            options.CircuitBreaker.SamplingDuration = total + total;
        }
    }

    /// <summary>Maps <c>/health</c> (readiness, with optional integrations) and <c>/alive</c> (liveness).</summary>
    public static WebApplication MapDefaultEndpoints(this WebApplication app)
    {
        app.MapHealthChecks("/health", HealthEndpoints.ReadinessOptions).AllowAnonymous();
        app.MapHealthChecks("/alive", HealthEndpoints.LivenessOptions).AllowAnonymous();
        return app;
    }
}
