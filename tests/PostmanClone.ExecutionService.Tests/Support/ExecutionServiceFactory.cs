using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using PostmanClone.TestKit;

namespace PostmanClone.ExecutionService.Tests.Support;

/// <summary>
/// Starts the real service on an in-memory test server. The settings are the minimum the service needs to start
/// (CONTRACTS §0: issuer and audience <c>PostmanClone</c>; the authority is never fetched by these tests). A test
/// passes the environment and any setting it changes, so each test owns its configuration.
/// </summary>
public sealed class ExecutionServiceFactory(
    string environment = "Development",
    IReadOnlyDictionary<string, string>? overrides = null) : WebApplicationFactory<Program>
{
    public const string UnusedMetadataAddress = "http://localhost/.well-known/openid-configuration";

    private readonly IReadOnlyDictionary<string, string> _settings = Defaults(overrides);

    private static Dictionary<string, string> Defaults(IReadOnlyDictionary<string, string>? overrides)
    {
        var settings = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["DATABASE_PROVIDER"] = "InMemory",
            ["Auth:MetadataAddress"] = UnusedMetadataAddress,
            ["Auth:Issuer"] = TestTokens.Issuer,
            ["Auth:Audience"] = TestTokens.Audience,
            // The loopback test authority is plain HTTP.
            ["Auth:RequireHttpsMetadata"] = "false",
        };
        foreach (var (key, value) in overrides ?? new Dictionary<string, string>())
        {
            settings[key] = value;
        }

        return settings;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(environment);
        foreach (var (key, value) in _settings)
        {
            builder.UseSetting(key, value);
        }
    }
}
