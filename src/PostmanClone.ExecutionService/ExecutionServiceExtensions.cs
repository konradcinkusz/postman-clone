using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using PostmanClone.ExecutionService.Clients;
using PostmanClone.ExecutionService.Data;
using PostmanClone.ExecutionService.Options;
using PostmanClone.ExecutionService.Sending.Assembly;
using PostmanClone.ExecutionService.Sending.Execution;
using PostmanClone.ExecutionService.Sending.Ssrf;
using PostmanClone.ExecutionService.Ssrf;
using PostmanClone.ServiceDefaults.Database;
using PostmanClone.ServiceDefaults.Integrations;

namespace PostmanClone.ExecutionService;

/// <summary>The ExecutionService's own wiring. <c>Program.cs</c> calls this once; the kernel is wired by the caller.</summary>
public static class ExecutionServiceExtensions
{
    public const string DatabaseName = "executiondb";

    public static WebApplicationBuilder AddExecutionService(this WebApplicationBuilder builder)
    {
        builder.Services.AddOptions<ExecutionOptions>()
            .Bind(builder.Configuration.GetSection(ExecutionOptions.SectionName))
            .ValidateOnStart();
        builder.Services.AddSingleton<IValidateOptions<ExecutionOptions>, ExecutionOptionsValidator>();
        builder.Services.AddOptionalIntegration<SsrfProtectionIntegration>();

        builder.Services.ConfigureHttpJsonOptions(json =>
            json.SerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase)));

        // The sender's pieces. Stateless ones are singletons; RequestExecutor is per request because the typed
        // EnvironmentsClient it holds is transient (a captured HttpClient would outlive its handler).
        builder.Services.AddSingleton(provider => new TargetValidator(provider.GetRequiredService<IOptions<ExecutionOptions>>().Value));
        builder.Services.AddSingleton(provider => new RedirectPlanner(provider.GetRequiredService<IOptions<ExecutionOptions>>().Value));
        builder.Services.AddSingleton(provider => new RequestAssembler(provider.GetRequiredService<IOptions<ExecutionOptions>>().Value, CryptographicRandomBytes.Instance));
        builder.Services.AddSingleton<IHostResolver, SystemHostResolver>();
        builder.Services.AddSingleton<IAddressDialer, SocketAddressDialer>();
        builder.Services.AddSingleton<IHistoryRecorder, NullHistoryRecorder>();
        builder.Services.AddScoped<RequestExecutor>();

        builder.Services.AddEnvironmentsClient(builder.Configuration);
        builder.AddDatabaseContext<ExecutionDbContext>(DatabaseName);
        return builder;
    }
}
