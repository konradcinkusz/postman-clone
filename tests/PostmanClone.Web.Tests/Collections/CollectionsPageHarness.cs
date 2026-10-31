using System.Security.Claims;
using Bunit;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.DependencyInjection;
using PostmanClone.Web.Api;
using PostmanClone.Web.Auth;
using PostmanClone.Web.Collections;
using PostmanClone.Web.Response;
using PostmanClone.Web.Tests.Auth;

namespace PostmanClone.Web.Tests.Collections;

/// <summary>
/// The workspace home rendered by bUnit with the real objects behind it: a real session in the real store, the real
/// <see cref="WorkspaceShell"/> and <see cref="CollectionsClient"/> (with the production bearer handler), the fake authservice
/// for the profile, and the fake CollectionsService behind the client's named HttpClient.
/// </summary>
internal static class CollectionsPageHarness
{
    public static BunitContext Create(AuthHarness harness, ClaimsPrincipal principal, FakeCollectionsService collections, IExecutionGateway? gateway = null)
    {
        var context = new BunitContext();
        var state = new WorkspaceState();
        var session = new CircuitSession(state, harness.Refresher);

        var services = new ServiceCollection();
        services.AddTransient<BearerTokenHandler>();
        services.AddHttpClient(BackendServices.CollectionsName, client => client.BaseAddress = new Uri("http://collections-service"))
            .AddHttpMessageHandler<BearerTokenHandler>()
            .ConfigurePrimaryHttpMessageHandler(() => collections.Handler);
        var client = new CollectionsClient(services.BuildServiceProvider().GetRequiredService<IHttpClientFactory>());

        context.Services.AddSingleton(state);
        context.Services.AddSingleton<AuthenticationStateProvider>(new FixedAuthentication(principal));
        context.Services.AddSingleton(session);
        var shell = new WorkspaceShell(state, session, client, harness.Client);
        context.Services.AddSingleton(shell);
        context.Services.AddSingleton(new RequestWorkbench(shell));
        // The sender (WP20) is registered as the host registers it; these page tests do not send, so the gateway answers nothing.
        var sender = gateway ?? new UnusedExecutionGateway();
        context.Services.AddSingleton(sender);
        context.Services.AddSingleton(new SendCoordinator(sender, new EnvironmentSelection()));
        return context;
    }

    private sealed class FixedAuthentication(ClaimsPrincipal principal) : AuthenticationStateProvider
    {
        public override Task<AuthenticationState> GetAuthenticationStateAsync() => Task.FromResult(new AuthenticationState(principal));
    }

    /// <summary>A gateway for pages that never send: any call answers that the sender is unavailable.</summary>
    private sealed class UnusedExecutionGateway : PostmanClone.Web.Response.IExecutionGateway
    {
        public Task<PostmanClone.Web.Api.CollectionAnswer<PostmanClone.Web.Api.ExecutionResultDto>> ExecuteAsync(PostmanClone.Web.Api.ExecuteBody body, CancellationToken cancellationToken) =>
            Task.FromResult(new PostmanClone.Web.Api.CollectionAnswer<PostmanClone.Web.Api.ExecutionResultDto>(PostmanClone.Web.Api.CollectionOutcome.Unavailable, null, null));
    }
}
