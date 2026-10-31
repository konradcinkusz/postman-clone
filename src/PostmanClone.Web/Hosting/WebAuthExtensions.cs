using System.Net.Http;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Server.Circuits;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using PostmanClone.Web.Api;
using PostmanClone.Web.Auth;
using PostmanClone.Web.Collections;
using PostmanClone.Web.Environments;
using PostmanClone.Web.Membership;
using PostmanClone.Web.Response;

namespace PostmanClone.Web.Hosting;

/// <summary>The Web's sign-in wiring: the session scheme, token verification, the sign-in state and its revalidation (WP12).</summary>
public static class WebAuthExtensions
{
    public static WebApplicationBuilder AddWebAuthentication(this WebApplicationBuilder builder)
    {
        builder.Services.AddOptions<AuthServiceOptions>()
            .Bind(builder.Configuration.GetSection(AuthServiceOptions.Section))
            .ValidateDataAnnotations()
            .ValidateOnStart();
        builder.Services.TryAddSingleton(TimeProvider.System);

        // The discovery document is read through the authservice client, so it resolves through service discovery.
        builder.Services.AddSingleton<IConfigurationManager<OpenIdConnectConfiguration>>(services =>
        {
            var settings = services.GetRequiredService<IOptions<AuthServiceOptions>>().Value;
            var client = services.GetRequiredService<IHttpClientFactory>().CreateClient(BackendServices.AuthServiceName);
            var retriever = new HttpDocumentRetriever(client) { RequireHttps = settings.RequireHttpsMetadata };
            return new ConfigurationManager<OpenIdConnectConfiguration>(settings.MetadataAddress, new OpenIdConnectConfigurationRetriever(), retriever);
        });

        builder.Services.AddSingleton<ISessionStore, ServerSessionStore>();
        builder.Services.AddSingleton<AccessTokenVerifier>();
        builder.Services.AddSingleton<TokenRefresher>();
        builder.Services.AddSingleton<SessionLifecycle>();
        builder.Services.AddSingleton<SessionRevalidator>();

        builder.Services.AddAuthentication(SessionAuthenticationDefaults.Scheme)
            .AddScheme<AuthenticationSchemeOptions, SessionAuthenticationHandler>(SessionAuthenticationDefaults.Scheme, _ => { });
        builder.Services.AddAuthorization();

        // Per circuit: the signed-in user, the workspace and the session's token route.
        builder.Services.AddScoped<AuthenticationStateProvider, RevalidatingAuthenticationStateProvider>();
        builder.Services.AddScoped<WorkspaceState>();
        builder.Services.AddScoped<CircuitSession>();
        builder.Services.AddScoped<MembershipService>();
        builder.Services.AddScoped<WorkspaceShell>();
        builder.Services.AddScoped<RequestWorkbench>();
        // Environments (WP21, ADR-0024): the circuit's calls and the environment the sends use (held in server memory, never a cookie).
        builder.Services.AddScoped<EnvironmentService>();
        // The sender (WP20, ADR-0023): one send per circuit, the environment chosen for it.
        builder.Services.AddScoped<EnvironmentSelection>();
        builder.Services.AddScoped<IExecutionGateway, SessionExecutionGateway>();
        builder.Services.AddScoped<SendCoordinator>();
        builder.Services.AddScoped<CircuitHandler, WorkspaceCircuitHandler>();
        return builder;
    }
}
