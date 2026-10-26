using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using PostmanClone.ServiceDefaults.Integrations;

namespace PostmanClone.ServiceDefaults.Startup;

/// <summary>
/// Prints which optional integrations are live when the process starts (P8), so the question "what is
/// degraded here?" is answered by the log and not by reading configuration.
/// </summary>
internal sealed class StartupBanner(IEnumerable<IOptionalIntegration> integrations) : IHostedService
{
    public static string Format(IEnumerable<IOptionalIntegration> integrations)
    {
        var lines = integrations.Select(integration => $"  {integration.Name}: {integration.Status}");
        return string.Join(Environment.NewLine, ["Optional integrations:", .. lines]);
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        Console.WriteLine(Format(integrations));
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

public static class StartupBannerExtensions
{
    public static IServiceCollection AddStartupBanner(this IServiceCollection services)
    {
        services.AddHostedService<StartupBanner>();
        return services;
    }
}
