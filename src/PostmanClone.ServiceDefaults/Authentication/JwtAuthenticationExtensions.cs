using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;

namespace PostmanClone.ServiceDefaults.Authentication;

/// <summary>
/// Bearer-token validation against authservice (ADR-0004, CONTRACTS §0). The signing keys come from the issuer's
/// discovery document at <c>Auth:MetadataAddress</c>, so a key rotation needs no redeploy. Only RS256 is accepted:
/// a token signed with a shared secret is refused whatever key the metadata publishes. Every endpoint requires a
/// token unless it says <c>AllowAnonymous</c>, so a forgotten attribute fails closed.
/// </summary>
public static class JwtAuthenticationExtensions
{
    /// <summary>Clock skew tolerated when checking <c>exp</c> and <c>nbf</c> (WP02).</summary>
    public static readonly TimeSpan ClockSkew = TimeSpan.FromSeconds(30);

    private static readonly string[] AllowedAlgorithms = [SecurityAlgorithms.RsaSha256];

    /// <summary>
    /// Registers JWT bearer authentication and the fallback authorization policy. Reads <c>Auth:MetadataAddress</c>,
    /// <c>Auth:Issuer</c> and <c>Auth:Audience</c>, which must be set; <c>Auth:RequireHttpsMetadata</c> defaults to
    /// true and is switched off only for the loopback development stack.
    /// </summary>
    public static IServiceCollection AddJwtAuthentication(this IServiceCollection services, IConfiguration configuration)
    {
        var metadataAddress = Required(configuration, "Auth:MetadataAddress");
        var issuer = Required(configuration, "Auth:Issuer");
        var audience = Required(configuration, "Auth:Audience");

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.MetadataAddress = metadataAddress;
                options.RequireHttpsMetadata = configuration.GetValue("Auth:RequireHttpsMetadata", true);
                // Keep the token's own claim names (sub, organization, organization:{id}:role), not the legacy mapping.
                options.MapInboundClaims = false;
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidIssuer = issuer,
                    ValidAudience = audience,
                    ValidAlgorithms = AllowedAlgorithms,
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidateIssuerSigningKey = true,
                    ValidateLifetime = true,
                    RequireExpirationTime = true,
                    ClockSkew = ClockSkew,
                };
            });

        // An endpoint with no authorization metadata needs a signed-in user. AllowAnonymous opts out (health probes).
        services.AddAuthorizationBuilder()
            .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());
        return services;
    }

    private static string Required(IConfiguration configuration, string key) =>
        configuration[key] is { Length: > 0 } value
            ? value
            : throw new InvalidOperationException($"Configuration key '{key}' is required.");
}
