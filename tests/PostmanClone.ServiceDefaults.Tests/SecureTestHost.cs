using Microsoft.AspNetCore.Builder;
using PostmanClone.ServiceDefaults.Authentication;
using PostmanClone.ServiceDefaults.RateLimiting;
using PostmanClone.ServiceDefaults.Security;
using PostmanClone.TestKit;

namespace PostmanClone.ServiceDefaults.Tests;

/// <summary>
/// Starts a host with the kernel's security wiring (JWT, rate limits, origin guard, security headers) as a service
/// wires it. Pass the tokens whose key the host should trust, or null and a metadata address to fetch keys from.
/// </summary>
internal static class SecureTestHost
{
    /// <summary>Not fetched when the host trusts a key directly; it only has to be configured.</summary>
    private const string UnusedMetadataAddress = "http://localhost/.well-known/openid-configuration";

    public static Task<WebApplication> StartAsync(
        TestTokens? trusted = null,
        Action<WebApplicationBuilder>? configure = null,
        Action<WebApplication>? map = null,
        string? metadataAddress = null)
    {
        return TestHost.StartAsync(
            builder =>
            {
                builder.Configuration["Auth:MetadataAddress"] = metadataAddress ?? UnusedMetadataAddress;
                builder.Configuration["Auth:Issuer"] = TestTokens.Issuer;
                builder.Configuration["Auth:Audience"] = TestTokens.Audience;
                // The loopback test authority is plain HTTP.
                builder.Configuration["Auth:RequireHttpsMetadata"] = "false";

                builder.Services.AddJwtAuthentication(builder.Configuration);
                builder.Services.AddRateLimitingDefaults(builder.Configuration);
                trusted?.TrustIn(builder.Services);
                configure?.Invoke(builder);
            },
            app =>
            {
                app.UseSecurityDefaults();
                map?.Invoke(app);
            });
    }
}
