using Microsoft.Extensions.DependencyInjection;

namespace PostmanClone.Web.Api;

/// <summary>The backend services the Web reads, by their logical service-discovery names (CONTRACTS §5).</summary>
public static class BackendServices
{
    /// <summary>The logical name of authservice. The metadata address and the auth client both use it (docs/AUTH.md).</summary>
    public const string AuthServiceName = "authservice";

    /// <summary>The logical name of CollectionsService. The collections client resolves its HttpClient by this name.</summary>
    public const string CollectionsName = "collections";

    public static readonly IReadOnlyList<BackendService> All =
    [
        new(AuthServiceName, "http://authservice"),
        new(CollectionsName, "http://collections-service"),
        new("environments", "http://environments-service"),
        new("execution", "http://execution-service"),
    ];

    /// <summary>
    /// One named HttpClient per backend. The kernel's resilience handler and service discovery apply to each of them
    /// (P2a). The bearer handler adds no token of its own: it forwards the one a request carries.
    /// </summary>
    public static IServiceCollection AddBackendClients(this IServiceCollection services)
    {
        services.AddTransient<BearerTokenHandler>();
        foreach (var backend in All)
        {
            services.AddHttpClient(backend.Name, client => client.BaseAddress = new Uri(backend.Address))
                .AddHttpMessageHandler<BearerTokenHandler>();
        }

        services.AddScoped<IBackendStatusReader, BackendStatusReader>();
        services.AddSingleton<AuthServiceClient>();
        services.AddSingleton<CollectionsClient>();
        return services;
    }
}

/// <summary>A backend's display name, which is also its HttpClient name, and its logical address.</summary>
public sealed record BackendService(string Name, string Address);
