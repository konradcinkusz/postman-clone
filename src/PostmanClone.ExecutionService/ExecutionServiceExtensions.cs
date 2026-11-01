using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PostmanClone.ExecutionService.Clients;
using PostmanClone.ExecutionService.Data;
using PostmanClone.ExecutionService.History;
using PostmanClone.ExecutionService.Options;
using PostmanClone.ExecutionService.Sending;
using PostmanClone.ExecutionService.Ssrf;
using PostmanClone.Sending.Execution;
using PostmanClone.Sending.Limits;
using PostmanClone.Sending.Ssrf;
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

        // The shared sender with the SSRF rules (ADR-0011). It is stateless, so it is a singleton. RequestExecutor is per
        // request because the typed EnvironmentsClient it holds is transient (a captured HttpClient would outlive its handler).
        builder.Services.AddSingleton(provider =>
        {
            var options = provider.GetRequiredService<IOptions<ExecutionOptions>>().Value;
            return new OutboundRules(options.Ssrf);
        });
        builder.Services.AddSingleton<IHostResolver, SystemHostResolver>();
        builder.Services.AddSingleton<IAddressDialer, SocketAddressDialer>();
        builder.Services.AddSingleton(provider => new OutboundSender(
            provider.GetRequiredService<IOptions<ExecutionOptions>>().Value.ToLimits(),
            provider.GetRequiredService<OutboundRules>(),
            provider.GetRequiredService<IHostResolver>(),
            provider.GetRequiredService<IAddressDialer>(),
            provider.GetRequiredService<ILogger<OutboundSender>>()));
        builder.Services.AddScoped<IHistoryRecorder, HistoryRecorder>();
        builder.Services.AddScoped<RequestExecutor>();

        builder.Services.AddEnvironmentsClient(builder.Configuration);
        builder.AddDatabaseContext<ExecutionDbContext>(DatabaseName);
        return builder;
    }
}
