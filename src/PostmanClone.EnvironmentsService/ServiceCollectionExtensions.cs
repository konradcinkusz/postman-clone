using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PostmanClone.EnvironmentsService.Data;
using PostmanClone.EnvironmentsService.Secrets;
using PostmanClone.ServiceDefaults.Authentication;
using PostmanClone.ServiceDefaults.Database;
using PostmanClone.ServiceDefaults.Integrations;
using PostmanClone.ServiceDefaults.RateLimiting;

namespace PostmanClone.EnvironmentsService;

/// <summary>The EnvironmentsService wiring, called from <c>Program.cs</c> (P1: the manifest only names the steps).</summary>
public static class ServiceCollectionExtensions
{
    /// <summary>The database name the AppHost gives this service's logical database (P3).</summary>
    public const string DatabaseConnectionName = "environmentsdb";

    public static WebApplicationBuilder AddEnvironmentsService(this WebApplicationBuilder builder)
    {
        builder.AddDatabaseContext<EnvironmentsDbContext>(DatabaseConnectionName);

        // The key is read once, when the protector is first needed. A missing or invalid key degrades secrets only.
        builder.Services.AddSingleton<ISecretProtector>(services => SecretProtectorFactory.Create(
            builder.Configuration[SecretProtectorFactory.KeySetting],
            services.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(SecretProtectorFactory))));
        builder.Services.AddOptionalIntegration<SecretsIntegration>();

        builder.Services.AddJwtAuthentication(builder.Configuration);
        builder.Services.AddRateLimitingDefaults(builder.Configuration);
        return builder;
    }
}
