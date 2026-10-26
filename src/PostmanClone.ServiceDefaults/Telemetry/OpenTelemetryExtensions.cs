using Microsoft.AspNetCore.Http;
using OpenTelemetry.Instrumentation.AspNetCore;
using OpenTelemetry.Instrumentation.Http;
using OpenTelemetry.Instrumentation.Runtime;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace PostmanClone.ServiceDefaults.Telemetry;

/// <summary>
/// Traces, metrics and logs (P15). Exported over OTLP only when <c>OTEL_EXPORTER_OTLP_ENDPOINT</c> is set;
/// without it the instrumentation still runs, so local runs cost nothing and nothing leaves the machine.
/// </summary>
public static class OpenTelemetryExtensions
{
    public const string OtlpEndpointSetting = "OTEL_EXPORTER_OTLP_ENDPOINT";

    public static TBuilder ConfigureOpenTelemetry<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        builder.Logging.AddOpenTelemetry(logging =>
        {
            logging.IncludeFormattedMessage = true;
            logging.IncludeScopes = true;
        });

        var telemetry = builder.Services.AddOpenTelemetry()
            .WithMetrics(metrics => metrics
                // ASP.NET Core and HttpClient publish their metrics as named meters (.NET 8+).
                .AddMeter("Microsoft.AspNetCore.Hosting", "Microsoft.AspNetCore.Server.Kestrel", "System.Net.Http")
                .AddRuntimeInstrumentation())
            .WithTracing(tracing => tracing
                // Probe traffic would otherwise dominate the traces (P15).
                .AddAspNetCoreInstrumentation(options => options.Filter = IsNotHealthProbe)
                .AddHttpClientInstrumentation());

        if (!string.IsNullOrWhiteSpace(builder.Configuration[OtlpEndpointSetting]))
        {
            telemetry.UseOtlpExporter();
        }

        return builder;
    }

    private static bool IsNotHealthProbe(HttpContext context) =>
        !context.Request.Path.StartsWithSegments("/health") && !context.Request.Path.StartsWithSegments("/alive");
}
