using System.Diagnostics;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenTelemetry;
using OpenTelemetry.Trace;
using Xunit;

namespace PostmanClone.ServiceDefaults.Tests.Telemetry;

/// <summary>Traces are recorded for real requests and not for health probes (P15).</summary>
public sealed class TelemetryTests
{
    [Fact]
    public async Task Requests_are_traced_but_health_probes_are_filtered_out_of_traces()
    {
        var finished = new CapturingProcessor();
        await using var app = await TestHost.StartAsync(
            builder => builder.Services.ConfigureOpenTelemetryTracerProvider(tracing => tracing.AddProcessor(finished)),
            map: app => app.MapGet("/traced", () => Results.Ok()));
        using var client = TestHost.ClientFor(app);

        (await client.GetAsync("/health", TestContext.Current.CancellationToken)).Dispose();
        (await client.GetAsync("/alive", TestContext.Current.CancellationToken)).Dispose();
        (await client.GetAsync("/traced", TestContext.Current.CancellationToken)).Dispose();

        // Only server spans are asked about: the test's own client spans are HttpClient instrumentation, not ASP.NET Core.
        var serverPaths = finished.Spans()
            .Where(span => span.Kind == ActivityKind.Server)
            .Select(span => span.GetTagItem("url.path") as string ?? span.GetTagItem("http.target") as string ?? string.Empty)
            .ToList();
        Assert.Contains(serverPaths, path => path == "/traced");
        Assert.DoesNotContain(serverPaths, path => path is "/health" or "/alive");
    }

    /// <summary>Records every finished span, so the test reads what the SDK would export.</summary>
    private sealed class CapturingProcessor : BaseProcessor<Activity>
    {
        private readonly List<Activity> _spans = [];

        public override void OnEnd(Activity data)
        {
            lock (_spans)
            {
                _spans.Add(data);
            }
        }

        public List<Activity> Spans()
        {
            lock (_spans)
            {
                return [.. _spans];
            }
        }
    }
}
