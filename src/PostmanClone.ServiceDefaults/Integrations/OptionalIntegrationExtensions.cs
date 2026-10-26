using Microsoft.Extensions.DependencyInjection;

namespace PostmanClone.ServiceDefaults.Integrations;

public static class OptionalIntegrationExtensions
{
    /// <summary>Registers an integration whose status appears on <c>/health</c> and in the startup banner.</summary>
    public static IServiceCollection AddOptionalIntegration<TIntegration>(this IServiceCollection services)
        where TIntegration : class, IOptionalIntegration
    {
        services.AddSingleton<IOptionalIntegration, TIntegration>();
        return services;
    }
}
