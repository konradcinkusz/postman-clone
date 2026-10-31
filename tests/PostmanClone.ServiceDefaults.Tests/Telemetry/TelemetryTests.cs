using System.Diagnostics;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenTelemetry;
using OpenTelemetry.Trace;
using PostmanClone.ServiceDefaults.Telemetry;
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

        // A server span ends after its response is written, so it can finish after the client has read the response.
        // Wait for the traced request's span (bounded), then the probes, which finished before it, are settled too.
        var serverPaths = await ServerPathsAfterAsync(finished, expectedPath: "/traced");
        Assert.Contains(serverPaths, path => path == "/traced");
        Assert.DoesNotContain(serverPaths, path => path is "/health" or "/alive");
    }

    [Fact]
    public async Task Outbound_requests_marked_as_user_targets_get_no_client_span()
    {
        var finished = new CapturingProcessor();
        await using var app = await TestHost.StartAsync(
            builder => builder.Services.ConfigureOpenTelemetryTracerProvider(tracing => tracing.AddProcessor(finished)),
            map: app => app.MapGet("/{name}", (string name) => Results.Ok(name)));
        using var client = TestHost.ClientFor(app);

        using var marked = new HttpRequestMessage(HttpMethod.Get, "/user-target-secret-path");
        marked.Options.Set(OutboundTelemetry.UserTarget, true);
        using var unmarked = new HttpRequestMessage(HttpMethod.Get, "/platform-call");
        (await client.SendAsync(marked, TestContext.Current.CancellationToken)).Dispose();
        (await client.SendAsync(unmarked, TestContext.Current.CancellationToken)).Dispose();

        var clientUrls = (await SettledClientUrlsAsync(finished, expectedUrlPart: "/platform-call")).ToList();
        Assert.Contains(clientUrls, url => url.Contains("/platform-call", StringComparison.Ordinal));
        Assert.DoesNotContain(clientUrls, url => url.Contains("user-target-secret-path", StringComparison.Ordinal));
    }

    [Fact]
    public void A_user_target_request_is_not_recorded_and_any_other_request_is_recorded()
    {
        using var marked = new HttpRequestMessage(HttpMethod.Get, "http://127.0.0.1/");
        marked.Options.Set(OutboundTelemetry.UserTarget, true);
        using var plain = new HttpRequestMessage(HttpMethod.Get, "http://127.0.0.1/");

        Assert.False(OutboundTelemetry.RecordsSpan(marked));
        Assert.True(OutboundTelemetry.RecordsSpan(plain));
    }

    /// <summary>Waits, bounded, until a span for the expected server path has finished, then returns the server paths seen.</summary>
    private static async Task<List<string>> ServerPathsAfterAsync(CapturingProcessor finished, string expectedPath)
    {
        var deadline = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(10);
        while (true)
        {
            var paths = finished.Spans()
                .Where(span => span.Kind == ActivityKind.Server)
                .Select(span => span.GetTagItem("url.path") as string ?? span.GetTagItem("http.target") as string ?? string.Empty)
                .ToList();
            if (paths.Contains(expectedPath) || DateTimeOffset.UtcNow > deadline)
            {
                return paths;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(20), TestContext.Current.CancellationToken);
        }
    }

    /// <summary>Waits, bounded, until a client span for the expected URL part has finished, then returns the client URLs seen.</summary>
    private static async Task<IReadOnlyList<string>> SettledClientUrlsAsync(CapturingProcessor finished, string expectedUrlPart)
    {
        var deadline = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(10);
        while (true)
        {
            var urls = finished.Spans()
                .Where(span => span.Kind == ActivityKind.Client)
                .Select(span => span.GetTagItem("url.full") as string ?? span.GetTagItem("http.url") as string ?? string.Empty)
                .ToList();
            if (urls.Any(url => url.Contains(expectedUrlPart, StringComparison.Ordinal)) || DateTimeOffset.UtcNow > deadline)
            {
                return urls;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(20), TestContext.Current.CancellationToken);
        }
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
