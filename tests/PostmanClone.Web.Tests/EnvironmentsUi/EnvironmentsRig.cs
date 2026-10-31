using System.Security.Claims;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using PostmanClone.Web.Api;
using PostmanClone.Web.Auth;
using PostmanClone.Web.Environments;
using PostmanClone.Web.Response;
using PostmanClone.Web.Tests.Auth;

namespace PostmanClone.Web.Tests.EnvironmentsUi;

/// <summary>
/// The environments UI rendered by bUnit with the real objects behind it (WP21): a real session in the real store, the real
/// <see cref="EnvironmentsClient"/> with the production bearer handler, the real <see cref="EnvironmentService"/> and
/// <see cref="EnvironmentSelection"/>, and the fake EnvironmentsService behind the client's named HttpClient.
/// </summary>
internal sealed class EnvironmentsRig : IDisposable
{
    private EnvironmentsRig(BunitContext context, WorkspaceState state, EnvironmentService service, EnvironmentSelection selection, FakeEnvironmentsService fake)
    {
        Context = context;
        State = state;
        Service = service;
        Selection = selection;
        Fake = fake;
    }

    public BunitContext Context { get; }

    public WorkspaceState State { get; }

    public EnvironmentService Service { get; }

    public EnvironmentSelection Selection { get; }

    public FakeEnvironmentsService Fake { get; }

    public static EnvironmentsRig Create(AuthHarness harness, ClaimsPrincipal principal, FakeEnvironmentsService fake)
    {
        var context = new BunitContext();
        var state = new WorkspaceState();
        state.Apply(principal);
        var session = new CircuitSession(state, harness.Refresher);
        var client = CreateClient(fake);
        var selection = new EnvironmentSelection();
        var service = new EnvironmentService(state, session, client, selection);

        context.Services.AddSingleton(state);
        context.Services.AddSingleton(session);
        context.Services.AddSingleton(client);
        context.Services.AddSingleton(selection);
        context.Services.AddSingleton(service);
        return new EnvironmentsRig(context, state, service, selection, fake);
    }

    /// <summary>
    /// Registers what the layout's top-bar selector injects (WP21), over a fake service that answers nothing for a signed-out
    /// visitor. The session is built on the harness's own refresher, as the host builds it.
    /// </summary>
    public static void AddSelectorServices(IServiceCollection services, AuthHarness harness)
    {
        var fake = new FakeEnvironmentsService();
        var client = CreateClient(fake);
        services.AddSingleton(harness.Refresher);
        services.AddScoped<CircuitSession>();
        services.AddScoped<EnvironmentSelection>();
        services.AddScoped(provider => new EnvironmentService(
            provider.GetRequiredService<WorkspaceState>(),
            provider.GetRequiredService<CircuitSession>(),
            client,
            provider.GetRequiredService<EnvironmentSelection>()));
    }

    /// <summary>The Web's environments client over the fake, through the production bearer handler (Api/BackendServices).</summary>
    public static EnvironmentsClient CreateClient(FakeEnvironmentsService fake)
    {
        var services = new ServiceCollection();
        services.AddTransient<BearerTokenHandler>();
        services.AddHttpClient(BackendServices.EnvironmentsName, client => client.BaseAddress = new Uri("http://environments-service"))
            .AddHttpMessageHandler<BearerTokenHandler>()
            .ConfigurePrimaryHttpMessageHandler(() => fake.Handler);
        return new EnvironmentsClient(services.BuildServiceProvider().GetRequiredService<IHttpClientFactory>());
    }

    public void Dispose() => Context.Dispose();
}
