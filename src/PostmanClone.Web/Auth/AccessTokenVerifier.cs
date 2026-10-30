using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using PostmanClone.ServiceDefaults.Authentication;

namespace PostmanClone.Web.Auth;

/// <summary>The claims of a verified access token, and when it stops being valid.</summary>
public sealed record VerifiedAccess(IReadOnlyList<Claim> Claims, DateTimeOffset ExpiresAt);

/// <summary>
/// Verifies an access token's signature against authservice's published keys, with issuer, audience and lifetime
/// (FRONTEND-BFF §4: decoding is not verifying). The keys come from the discovery document, so a rotation needs no
/// restart. Only RS256 is accepted. The claims keep their own names, as the kernel's do (WorkspaceClaims).
/// </summary>
public sealed class AccessTokenVerifier(IConfigurationManager<OpenIdConnectConfiguration> metadata, IOptions<AuthServiceOptions> options)
{
    private static readonly string[] AllowedAlgorithms = [Microsoft.IdentityModel.Tokens.SecurityAlgorithms.RsaSha256];

    /// <summary>The verified claims, or null when the token does not verify or the keys cannot be read.</summary>
    public async Task<VerifiedAccess?> VerifyAsync(string accessToken, CancellationToken cancellationToken)
    {
        OpenIdConnectConfiguration configuration;
        try
        {
            configuration = await metadata.GetConfigurationAsync(cancellationToken);
        }
        catch (InvalidOperationException)
        {
            // The discovery document could not be read. The sign-in fails rather than trusting an unverified token.
            return null;
        }

        var settings = options.Value;
        var parameters = new TokenValidationParameters
        {
            ValidIssuer = settings.Issuer,
            ValidAudience = settings.Audience,
            ValidAlgorithms = AllowedAlgorithms,
            IssuerSigningKeys = configuration.SigningKeys,
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateIssuerSigningKey = true,
            ValidateLifetime = true,
            RequireExpirationTime = true,
            RequireSignedTokens = true,
            ClockSkew = JwtAuthenticationExtensions.ClockSkew,
        };

        var handler = new JwtSecurityTokenHandler { MapInboundClaims = false };
        try
        {
            var principal = handler.ValidateToken(accessToken, parameters, out var validated);
            return new VerifiedAccess(principal.Claims.ToList(), new DateTimeOffset(((JwtSecurityToken)validated).ValidTo));
        }
        catch (Exception exception) when (exception is SecurityTokenException or ArgumentException)
        {
            return null;
        }
    }
}
