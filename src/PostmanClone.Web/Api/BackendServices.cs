using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;

namespace PostmanClone.Web.Api;

/// <summary>The backend services the Web reads, by their logical service-discovery names (CONTRACTS §5).</summary>
public static class BackendServices
{
    /// <summary>The logical name of authservice. The metadata address and the auth client both use it (docs/AUTH.md).</summary>
    public const string AuthServiceName = "authservice";

    /// <summary>The logical name of CollectionsService. The collections client resolves its HttpClient by this name.</summary>
    public const string CollectionsName = "collections";

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
        new("environments", "http://environments-service"),
        new(ExecutionName, "http://execution-service"),
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

        ConfigureExecution(services);

        services.AddScoped<IBackendStatusReader, BackendStatusReader>();
        services.AddSingleton<AuthServiceClient>();
        services.AddSingleton<CollectionsClient>();
        services.AddSingleton<IExecutionClient, ExecutionClient>();
        return services;
    }

    /// <summary>
    /// The kernel's one resilience handler, with the execute client's own limits (ADR-0022). The handler is the kernel's; only
    /// its options are named for this client, so the timeouts of the other backends do not change. A send is a POST, which the
    /// handler never retries: a retry would send a third party's request twice.
    /// </summary>
    private static void ConfigureExecution(IServiceCollection services) => services.Configure<HttpStandardResilienceOptions>(ExecutionName, options =>
    {
        options.AttemptTimeout.Timeout = ExecutionTimeout;
        options.TotalRequestTimeout.Timeout = ExecutionTimeout;
        // The circuit breaker must sample for at least twice the attempt timeout, or the handler refuses to start.
        options.CircuitBreaker.SamplingDuration = ExecutionTimeout + ExecutionTimeout;
        options.Retry.DisableForUnsafeHttpMethods();
    });
}

/// <summary>A backend's display name, which is also its HttpClient name, and its logical address.</summary>
public sealed record BackendService(string Name, string Address);
