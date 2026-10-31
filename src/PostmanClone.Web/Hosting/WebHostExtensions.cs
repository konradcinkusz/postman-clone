using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Components.Server;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.SignalR;
using PostmanClone.ServiceDefaults.Security;
using PostmanClone.Web.Api;
using PostmanClone.Web.Auth;
using PostmanClone.Web.Collections;
using PostmanClone.Web.Components;
using PostmanClone.Web.Membership;

namespace PostmanClone.Web.Hosting;

/// <summary>The Web host's wiring: services, the request pipeline and the component endpoints (BLAZOR-WEB-APP §3, §8).</summary>
public static class WebHostExtensions
{
    /// <summary>
    /// The largest message a circuit accepts from the browser. The default is 32 KB, which is too small for a
    /// pasted request body: the editor sends the text as one input event. 2 MB covers a 1 MB raw body with the
    /// JSON escaping and framing around it (WP07, the sizes CONTRACTS §4 allows for a request body).
    /// </summary>
    public const int MaximumCircuitMessageBytes = 2 * 1024 * 1024;

    /// <summary>How long a disconnected circuit keeps its state, so a page reload within this window resumes it.</summary>
    public static readonly TimeSpan DisconnectedCircuitRetention = TimeSpan.FromMinutes(3);

    public static WebApplicationBuilder AddWebServices(this WebApplicationBuilder builder)
    {
        builder.Services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost;
            // Behind a reverse proxy, list its addresses; loopback is trusted by default.
            foreach (var proxy in builder.Configuration.GetSection("ForwardedHeaders:KnownProxies").Get<string[]>() ?? [])
            {
                options.KnownProxies.Add(IPAddress.Parse(proxy));
            }
        });

        builder.Services.AddRazorComponents()
            .AddInteractiveServerComponents(options => options.DisconnectedCircuitRetentionPeriod = DisconnectedCircuitRetention);
        // ComponentHub is internal; the global HubOptions apply to it as its defaults.
        builder.Services.Configure<HubOptions>(options => options.MaximumReceiveMessageSize = MaximumCircuitMessageBytes);

        builder.Services.AddAntiforgery();
        builder.AddWebAuthentication();
        builder.Services.AddBackendClients();
        return builder;
    }

    /// <summary>
    /// Forwarded headers first, so the origin guard compares the public host; then the browser guards; then the session
    /// (authentication and authorization); then antiforgery, which protects the form posts of the sign-in flow.
    /// </summary>
    public static WebApplication UseWebPipeline(this WebApplication app)
    {
        app.UseForwardedHeaders();
        app.UseMiddleware<SecurityHeadersMiddleware>();
        app.UseMiddleware<OriginGuardMiddleware>();
        app.UseMiddleware<ContentSecurityPolicyMiddleware>();
        app.UseAuthentication();
        app.UseAuthorization();
        app.UseAntiforgery();
        return app;
    }

    /// <summary>The static assets, the sign-in, membership and export endpoints, and the root components, both in interactive server render mode.</summary>
    public static WebApplication MapWebComponents(this WebApplication app)
    {
        app.MapStaticAssets();
        app.MapAuthEndpoints();
        app.MapMembershipEndpoints();
        app.MapCollectionDownloadEndpoints();
        app.MapRazorComponents<App>().AddInteractiveServerRenderMode();
        return app;
    }
}
