using Microsoft.Extensions.DependencyInjection;

namespace PostmanClone.Web.Api;

/// <summary>The backend services the Web reads, by their logical service-discovery names (CONTRACTS §5).</summary>
public static class BackendServices
{
    public static readonly IReadOnlyList<BackendService> All =
    [
        new("authservice", "http://authservice"),
        new("collections", "http://collections-service"),
        new("environments", "http://environments-service"),
        new("execution", "http://execution-service"),
    ];

    /// <summary>
    /// One named HttpClient per backend. The kernel's resilience handler and service discovery apply to each of them
    /// (P2a): this method adds no handler of its own.
    /// </summary>
    public static IServiceCollection AddBackendClients(this IServiceCollection services)
    {
        foreach (var backend in All)
        {
            services.AddHttpClient(backend.Name, client => client.BaseAddress = new Uri(backend.Address));
        }

        services.AddScoped<IBackendStatusReader, BackendStatusReader>();
        return services;
    }
}

/// <summary>A backend's display name, which is also its HttpClient name, and its logical address.</summary>
public sealed record BackendService(string Name, string Address);
