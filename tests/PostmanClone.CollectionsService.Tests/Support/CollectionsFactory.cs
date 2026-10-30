using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using PostmanClone.CollectionsService;
using PostmanClone.CollectionsService.Data;
using PostmanClone.ServiceDefaults.Database;
using PostmanClone.TestKit;

namespace PostmanClone.CollectionsService.Tests.Support;

/// <summary>
/// Hosts the real CollectionsService (its <c>Program</c>) in memory. The token issuer is <see cref="TestTokens"/>, whose
/// key the host trusts directly, so no metadata endpoint is needed. Pass a PostgreSQL connection string to run the
/// service against a real database; otherwise it uses the InMemory provider.
/// </summary>
/// <param name="tokens">Issues the tokens the host trusts.</param>
/// <param name="postgresConnectionString">When set, the host uses PostgreSQL with this connection string.</param>
/// <param name="clock">When set, the host reads time from it instead of the system clock.</param>
/// <param name="environment">When set, the host runs in this environment (the default is Development).</param>
/// <param name="interceptor">When set, every command the host's context runs passes through it.</param>
public sealed class CollectionsFactory(
    TestTokens tokens,
    string? postgresConnectionString = null,
    TimeProvider? clock = null,
    string? environment = null,
    DbCommandInterceptor? interceptor = null) : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting(DatabaseExtensions.ProviderSetting,
            postgresConnectionString is null ? DatabaseExtensions.InMemoryProvider : DatabaseExtensions.PostgreSqlProvider);
        if (postgresConnectionString is not null)
        {
            builder.UseSetting($"ConnectionStrings:{ServiceCollectionExtensions.DatabaseConnectionName}", postgresConnectionString);
        }

        if (environment is not null)
        {
            builder.UseEnvironment(environment);
        }

        // Plain-HTTP metadata address that is never fetched: the trusted key below replaces discovery.
        builder.UseSetting("Auth:MetadataAddress", "http://localhost/.well-known/openid-configuration");
        builder.UseSetting("Auth:Issuer", TestTokens.Issuer);
        builder.UseSetting("Auth:Audience", TestTokens.Audience);
        builder.UseSetting("Auth:RequireHttpsMetadata", "false");

        builder.ConfigureTestServices(services =>
        {
            tokens.TrustIn(services);

            // Registered after the service's own TimeProvider, so the test clock is the one the services resolve.
            if (clock is not null)
            {
                services.AddSingleton(clock);
            }

            if (interceptor is not null)
            {
                services.ConfigureDbContext<CollectionsDbContext>(options => options.AddInterceptors(interceptor));
            }
        });
    }
}
