using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace PostmanClone.ExecutionService.Clients;

public static class EnvironmentsClientRegistration
{
    /// <summary>
    /// Registers <see cref="EnvironmentsClient"/> with its base address and timeout, and with redirects switched off:
    /// a 3xx from EnvironmentsService is an upstream failure, not a second request to wherever it points.
    /// The kernel's single resilience handler applies (P2a); nothing is added here.
    /// </summary>
    public static IServiceCollection AddEnvironmentsClient(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<EnvironmentsClientOptions>()
            .Bind(configuration.GetSection(EnvironmentsClientOptions.SectionName));

        services.AddHttpClient<EnvironmentsClient>((provider, client) =>
            {
                var options = provider.GetRequiredService<IOptions<EnvironmentsClientOptions>>().Value;
                client.BaseAddress = options.BaseAddress;
                client.Timeout = options.Timeout;
            })
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { AllowAutoRedirect = false });

        return services;
    }
}
