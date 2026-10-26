using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Hosting;
using PostmanClone.TestKit;
using Xunit;

namespace PostmanClone.ServiceDefaults.Tests.Http;

/// <summary>Every <c>HttpClient</c> gets exactly one resilience handler, the kernel's (P2).</summary>
public sealed class ResilienceTests
{
    [Fact]
    public void A_configured_client_has_exactly_one_resilience_handler()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.AddServiceDefaults();
        var recorder = new HandlerRecorder();
        builder.Services.AddSingleton<IHttpMessageHandlerBuilderFilter>(recorder);
        builder.Services.AddHttpClient("probe");

        using var host = builder.Build();
        using var client = host.Services.GetRequiredService<IHttpClientFactory>().CreateClient("probe");
        Assert.NotNull(client);

        var resilienceHandlers = recorder.HandlerTypeNames.Count(name => name.StartsWith("Microsoft.Extensions.Http.Resilience", StringComparison.Ordinal));
        Assert.Equal(1, resilienceHandlers);
    }

    [Fact]
    public async Task A_client_built_by_the_kernel_completes_a_request_through_its_handler_chain()
    {
        var fake = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("pong") });
        var builder = Host.CreateApplicationBuilder();
        builder.AddServiceDefaults();
        builder.Services.AddHttpClient("probe").ConfigurePrimaryHttpMessageHandler(() => fake);

        using var host = builder.Build();
        using var client = host.Services.GetRequiredService<IHttpClientFactory>().CreateClient("probe");
        using var response = await client.GetAsync("http://probe.internal/ping", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("pong", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Single(fake.Requests);
    }

    private sealed class HandlerRecorder : IHttpMessageHandlerBuilderFilter
    {
        public List<string> HandlerTypeNames { get; } = [];

        public Action<HttpMessageHandlerBuilder> Configure(Action<HttpMessageHandlerBuilder> next) => builder =>
        {
            next(builder);
            if (builder.Name == "probe")
            {
                HandlerTypeNames.AddRange(builder.AdditionalHandlers.Select(handler => handler.GetType().FullName ?? string.Empty));
            }
        };
    }
}
