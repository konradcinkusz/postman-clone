using Microsoft.Extensions.DependencyInjection;
using PostmanClone.ServiceDefaults.Resilience;

namespace PostmanClone.Web.Api;

/// <summary>The backend services the Web reads, by their logical service-discovery names (CONTRACTS §5).</summary>
public static class BackendServices
{
    /// <summary>The logical name of authservice. The metadata address and the auth client both use it (docs/AUTH.md).</summary>
    public const string AuthServiceName = "authservice";

    /// <summary>The logical name of CollectionsService. The collections client resolves its HttpClient by this name.</summary>
    public const string CollectionsName = "collections";

    /// <summary>The logical name of EnvironmentsService. The environments client resolves its HttpClient by this name (WP21).</summary>
    public const string EnvironmentsName = "environments";

    /// <summary>The logical name of ExecutionService. Its resilience options are longer than the others (see <see cref="ConfigureExecution"/>).</summary>
    public const string ExecutionName = "execution";

    /// <summary>
    /// How long one send may take end to end. The sender's own deadline is 60 s (CONTRACTS §4), plus the environment lookup
    /// and the history write, so the Web waits 120 s before it gives up on the answer.
    /// </summary>
    public static readonly TimeSpan ExecutionTimeout = TimeSpan.FromSeconds(120);

    public static readonly IReadOnlyList<BackendService> All =
    [
        new(AuthServiceName, "http://authservice"),
        new(CollectionsName, "http://collections-service"),
        new(EnvironmentsName, "http://environments-service"),
        new(ExecutionName, "http://execution-service"),
    ];

    /// <summary>
    /// One named HttpClient per backend. The kernel's resilience handler and service discovery apply to each of them
    /// (P2a). The bearer handler adds no token of its own: it forwards the one a request carries.
    /// </summary>
    public static IServiceCollection AddBackendClients(this IServiceCollection services, TimeSpan? executionTimeout = null)
    {
        services.AddTransient<BearerTokenHandler>();
        foreach (var backend in All)
        {
            services.AddHttpClient(backend.Name, client => client.BaseAddress = new Uri(backend.Address))
                .AddHttpMessageHandler<BearerTokenHandler>();
        }

        ConfigureExecution(services, executionTimeout ?? ExecutionTimeout);

        services.AddScoped<IBackendStatusReader, BackendStatusReader>();
        services.AddSingleton<AuthServiceClient>();
        services.AddSingleton<CollectionsClient>();
        services.AddSingleton<EnvironmentsClient>();
        services.AddSingleton<IExecutionClient, ExecutionClient>();
        return services;
    }

    /// <summary>
    /// The execute client's total timeout (ADR-0023). The kernel's one resilience handler reads it for this client by name; the
    /// handler is not added here, so no second one exists (architecture rule R9).
    /// </summary>
    private static void ConfigureExecution(IServiceCollection services, TimeSpan timeout) =>
        services.Configure<HttpClientTimeouts>(timeouts => timeouts.Totals[ExecutionName] = timeout);
}

/// <summary>A backend's display name, which is also its HttpClient name, and its logical address.</summary>
public sealed record BackendService(string Name, string Address);
