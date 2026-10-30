using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using PostmanClone.ExecutionService.Clients;
using PostmanClone.ExecutionService.Data;
using PostmanClone.ExecutionService.Options;
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

        builder.Services.AddEnvironmentsClient(builder.Configuration);
        builder.AddDatabaseContext<ExecutionDbContext>(DatabaseName);
        return builder;
    }
}
