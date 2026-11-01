using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PostmanClone.EnvironmentsService.Agent;
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
        // The agent-only scheme (CONTRACTS A2.4). The default scheme stays the JWT one, so agent tokens are unknown elsewhere.
        builder.Services.AddAuthentication()
            .AddScheme<AuthenticationSchemeOptions, AgentTokenAuthenticationHandler>(AgentTokenAuthenticationDefaults.Scheme, _ => { });
        builder.Services.AddOptions<AgentOptions>()
            .Bind(builder.Configuration.GetSection(AgentOptions.SectionName))
            .Validate(options => AgentOptionsValidator.Problems(options.AllowedWebOrigins).Count == 0,
                "Agent:AllowedWebOrigins holds an entry that is not an exact origin such as https://app.example.")
            .ValidateOnStart();
        builder.Services.AddRateLimitingDefaults(builder.Configuration);
        // Timestamps come from the injected clock (CONTRACTS §0); tests replace it with a fake.
        builder.Services.TryAddSingleton(TimeProvider.System);
        return builder;
    }
}
