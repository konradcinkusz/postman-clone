using System.Net;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using PostmanClone.Web.Api;

namespace PostmanClone.Web.Tests.Api;

/// <summary>The status reader: each backend's /health read into a state, and a missing backend never hides the others.</summary>
public sealed class BackendStatusReaderTests
{
    [Fact]
    public async Task Each_backend_is_read_into_its_state_from_its_health_payload()
    {
        var reader = BuildReader(host => host switch
        {
            "authservice" => Respond(HttpStatusCode.OK, "{\"status\":\"healthy\"}"),
            "environments-service" => Respond(HttpStatusCode.ServiceUnavailable, "{\"status\":\"unhealthy\",\"secrets\":\"unavailable\"}"),
            "execution-service" => Respond(HttpStatusCode.OK, "not json"),
            _ => throw new HttpRequestException("no such host"),
        });

        var statuses = await reader.ReadAsync(TestContext.Current.CancellationToken);

        Assert.Equal(
            [
                new BackendStatus("authservice", "healthy"),
                new BackendStatus("collections", BackendStates.Unreachable),
                new BackendStatus("environments", "unhealthy"),
                new BackendStatus("execution", BackendStates.Unknown),
            ],
            statuses);
    }

    [Fact]
    public void Each_backend_is_addressed_by_its_logical_name_from_contracts_section_5()
    {
        Assert.Equal(
            ["http://authservice", "http://collections-service", "http://environments-service", "http://execution-service"],
            BackendServices.All.Select(backend => backend.Address));
    }

    [Theory]
    [InlineData("healthy", "badge-success")]
    [InlineData("degraded", "badge-warning")]
    [InlineData("unhealthy", "badge-danger")]
    [InlineData("unreachable", "badge-danger")]
    [InlineData("unknown", "")]
    public void A_state_is_styled_by_what_it_means(string state, string expectedClass)
    {
        Assert.Equal(expectedClass, BackendStates.BadgeFor(state));
    }

    private static IBackendStatusReader BuildReader(Func<string, HttpResponseMessage> respond)
    {
        var services = new ServiceCollection();
        services.AddBackendClients();
        services.ConfigureHttpClientDefaults(builder => builder.ConfigurePrimaryHttpMessageHandler(() => new StubHandler(respond)));
        return services.BuildServiceProvider().CreateScope().ServiceProvider.GetRequiredService<IBackendStatusReader>();
    }

    private static HttpResponseMessage Respond(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private sealed class StubHandler(Func<string, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(respond(request.RequestUri!.Host));
    }
}
