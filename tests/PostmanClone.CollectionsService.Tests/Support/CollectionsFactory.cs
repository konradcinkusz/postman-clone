using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using PostmanClone.CollectionsService;
using PostmanClone.ServiceDefaults.Database;
using PostmanClone.TestKit;

namespace PostmanClone.CollectionsService.Tests.Support;

/// <summary>
/// Hosts the real CollectionsService (its <c>Program</c>) in memory. The token issuer is <see cref="TestTokens"/>, whose
/// key the host trusts directly, so no metadata endpoint is needed. Pass a PostgreSQL connection string to run the
/// service against a real database; otherwise it uses the InMemory provider.
/// </summary>
public sealed class CollectionsFactory(TestTokens tokens, string? postgresConnectionString = null) : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting(DatabaseExtensions.ProviderSetting,
            postgresConnectionString is null ? DatabaseExtensions.InMemoryProvider : DatabaseExtensions.PostgreSqlProvider);
        if (postgresConnectionString is not null)
        {
            builder.UseSetting($"ConnectionStrings:{ServiceCollectionExtensions.DatabaseConnectionName}", postgresConnectionString);
        }

        // Plain-HTTP metadata address that is never fetched: the trusted key below replaces discovery.
        builder.UseSetting("Auth:MetadataAddress", "http://localhost/.well-known/openid-configuration");
        builder.UseSetting("Auth:Issuer", TestTokens.Issuer);
        builder.UseSetting("Auth:Audience", TestTokens.Audience);
        builder.UseSetting("Auth:RequireHttpsMetadata", "false");

        builder.ConfigureTestServices(services => tokens.TrustIn(services));
    }
}
