using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Logging;
using PostmanClone.EnvironmentsService.Data;
using PostmanClone.ServiceDefaults.Database;
using PostmanClone.TestKit;

namespace PostmanClone.EnvironmentsService.Tests;

/// <summary>
/// Runs the real service in-process. Tokens are trusted through <see cref="TestTokens.TrustIn"/>, so no discovery
/// endpoint is needed. The database is InMemory unless the settings name PostgreSQL.
/// </summary>
public sealed class EnvironmentsServiceFactory(
    TestTokens tokens,
    IReadOnlyDictionary<string, string?> settings,
    ILoggerProvider? logs = null,
    TimeProvider? clock = null) : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting(DatabaseExtensions.ProviderSetting, DatabaseExtensions.InMemoryProvider);
        builder.UseSetting("Auth:MetadataAddress", "https://authservice.test/.well-known/openid-configuration");
        builder.UseSetting("Auth:Issuer", TestTokens.Issuer);
        builder.UseSetting("Auth:Audience", TestTokens.Audience);
        foreach (var (key, value) in settings)
        {
            builder.UseSetting(key, value);
        }

        builder.ConfigureTestServices(services =>
        {
            tokens.TrustIn(services);
            if (clock is not null)
            {
                // The service type is named explicitly: AddSingleton(clock) would register the runtime type (a fake).
                services.AddSingleton<TimeProvider>(clock);
            }
        });
        if (logs is not null)
        {
            // Trace, so the log-capture test sees every line the service could write, not only the default levels.
            builder.ConfigureLogging(logging =>
            {
                logging.SetMinimumLevel(LogLevel.Trace);
                logging.AddProvider(logs);
            });
        }
    }

    /// <summary>Waits until the hosted migration has applied the schema (or failed, which throws).</summary>
    public async Task WaitForSchemaAsync(CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(60));
        await Services.GetRequiredService<MigrationCompletionSignal>().WaitAsync(timeout.Token);
    }
}
