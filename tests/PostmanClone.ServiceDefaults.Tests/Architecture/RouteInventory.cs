extern alias collections;
extern alias environments;
extern alias execution;
extern alias web;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using PostmanClone.TestKit;

namespace PostmanClone.ServiceDefaults.Tests.Architecture;

/// <summary>
/// Hosts one composition root (its <c>Program</c>) in memory, in the Production environment, with the minimum settings
/// the root needs to start. Nothing is listened on and no backend is contacted: only the endpoint table is read.
/// </summary>
public sealed class InventoryHost<TProgram>(IReadOnlyDictionary<string, string?> settings) : WebApplicationFactory<TProgram>
    where TProgram : class
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Production");
        foreach (var (key, value) in settings)
        {
            builder.UseSetting(key, value);
        }
    }
}

/// <summary>
/// Reads the endpoint table of every composition root from its <c>EndpointDataSource</c>. One record per (service,
/// method, route); the flags say what the endpoint declares and what the host's fallback policy would apply to it.
/// </summary>
public static class RouteInventory
{
    /// <summary>The settings every root needs to start in memory. Tokens are never fetched: the authority is a dead address.</summary>
    private static readonly IReadOnlyDictionary<string, string?> CommonSettings = new Dictionary<string, string?>(StringComparer.Ordinal)
    {
        ["DATABASE_PROVIDER"] = "InMemory",
        ["Auth:MetadataAddress"] = "http://localhost/.well-known/openid-configuration",
        ["Auth:Issuer"] = TestTokens.Issuer,
        ["Auth:Audience"] = TestTokens.Audience,
        ["Auth:RequireHttpsMetadata"] = "false",
    };

    private static readonly Lazy<IReadOnlyList<RouteFact>> Everything = new(ReadAll);

    /// <summary>Every route of every service and the Web, read once per test run.</summary>
    public static IReadOnlyList<RouteFact> All => Everything.Value;

    public static IReadOnlyList<RouteFact> For(string service) => All.Where(route => route.Service == service).ToList();

    private static IReadOnlyList<RouteFact> ReadAll() =>
    [
        .. Read<collections::Program>("CollectionsService"),
        .. Read<environments::Program>("EnvironmentsService"),
        .. Read<execution::Program>("ExecutionService"),
        .. Read<web::Program>("Web"),
    ];

    private static IReadOnlyList<RouteFact> Read<TProgram>(string service) where TProgram : class
    {
        using var host = new InventoryHost<TProgram>(CommonSettings);
        var services = host.Services;
        var fallbackRequiresUser = services.GetService<IOptions<AuthorizationOptions>>()?.Value.FallbackPolicy?.Requirements
            .Any(requirement => requirement is DenyAnonymousAuthorizationRequirement) ?? false;

        return services.GetServices<EndpointDataSource>()
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .SelectMany(endpoint =>
            {
                var methods = endpoint.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods ?? new[] { "ANY" };
                var route = endpoint.RoutePattern.RawText ?? string.Empty;
                return methods.Select(method => new RouteFact(
                    service,
                    method,
                    route,
                    AllowsAnonymous: endpoint.Metadata.GetMetadata<IAllowAnonymous>() is not null,
                    HasAuthorizeMetadata: endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>().Count > 0,
                    FallbackRequiresUser: fallbackRequiresUser,
                    FrameworkOwned: IsFrameworkOwned(endpoint, route)));
            })
            // The same (method, route) can come from more than one data source; a route counts as anonymous if any of them is.
            .GroupBy(route => (route.Method, route.Route))
            .Select(group => new RouteFact(
                service,
                group.Key.Method,
                group.Key.Route,
                AllowsAnonymous: group.Any(route => route.AllowsAnonymous),
                HasAuthorizeMetadata: group.Any(route => route.HasAuthorizeMetadata),
                FallbackRequiresUser: group.Any(route => route.FallbackRequiresUser),
                FrameworkOwned: group.Any(route => route.FrameworkOwned)))
            .OrderBy(route => route.Route, StringComparer.Ordinal)
            .ThenBy(route => route.Method, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>
    /// The framework's own endpoints: static web assets (their descriptor), the static-file fallback (<c>{**path:file}</c>,
    /// its fallback metadata), and the Blazor runtime (<c>/_blazor</c> circuit and negotiation, <c>/_framework</c> scripts).
    /// Their access is the framework's: a circuit is established anonymously and every component checks its own
    /// authorization. No application endpoint carries any of these markers.
    /// </summary>
    private static bool IsFrameworkOwned(RouteEndpoint endpoint, string route)
    {
        if (route.StartsWith("/_blazor", StringComparison.Ordinal) || route.StartsWith("/_framework/", StringComparison.Ordinal))
        {
            return true;
        }

        return endpoint.Metadata.Any(item =>
            item.GetType().FullName is "Microsoft.AspNetCore.StaticAssets.StaticAssetDescriptor" or "Microsoft.AspNetCore.Routing.FallbackMetadata");
    }
}
