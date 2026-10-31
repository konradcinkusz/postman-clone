using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace PostmanClone.TestKit;

/// <summary>
/// Issues access tokens shaped like authservice's (CONTRACTS §0): <c>sub</c>, one <c>organization</c> claim per
/// membership and <c>organization:{id}:role</c>. Each instance has its own RSA key, so a token from another instance
/// comes from an untrusted issuer. Expiry is relative to the injected clock, never a calendar date.
/// </summary>
public sealed class TestTokens : IDisposable
{
    public const string Issuer = "https://authservice.test";
    public const string Audience = "PostmanClone";

    private const string KeyId = "test-key";

    private readonly TimeProvider _clock;
    private readonly RSA _rsa = RSA.Create(2048);

    public TestTokens(TimeProvider? clock = null) => _clock = clock ?? TimeProvider.System;

    /// <summary>The claims of one membership, as authservice issues them.</summary>
    public static IEnumerable<Claim> Membership(Guid organization, string role) =>
    [
        new Claim("organization", organization.ToString()),
        new Claim($"organization:{organization}:role", role),
    ];

    /// <summary>An RS256 token signed with this instance's key. Valid for an hour unless <paramref name="expiresIn"/> says otherwise.</summary>
    public string Issue(IEnumerable<Claim>? claims = null, TimeSpan? expiresIn = null, string issuer = Issuer, string audience = Audience)
    {
        var credentials = new SigningCredentials(new RsaSecurityKey(_rsa) { KeyId = KeyId }, SecurityAlgorithms.RsaSha256);
        return Write(new JwtSecurityToken(new JwtHeader(credentials), Payload(claims, expiresIn, issuer, audience)));
    }

    /// <summary>The same claims with <c>alg: none</c> and no signature.</summary>
    public string IssueUnsigned(IEnumerable<Claim>? claims = null) =>
        Write(new JwtSecurityToken(new JwtHeader(), Payload(claims, null, Issuer, Audience)));

    /// <summary>
    /// The same claims signed with HMAC-SHA256, keyed by this instance's public modulus: the classic forgery of a
    /// verifier that trusts the algorithm named in the token.
    /// </summary>
    public string IssueHmacForgery(IEnumerable<Claim>? claims = null)
    {
        var secret = new SymmetricSecurityKey(_rsa.ExportParameters(includePrivateParameters: false).Modulus!);
        var credentials = new SigningCredentials(secret, SecurityAlgorithms.HmacSha256);
        return Write(new JwtSecurityToken(new JwtHeader(credentials), Payload(claims, null, Issuer, Audience)));
    }

    /// <summary>The public key as a JWKS document, for a test discovery endpoint.</summary>
    public string Jwks()
    {
        var parameters = _rsa.ExportParameters(includePrivateParameters: false);
        var key = new
        {
            kty = "RSA",
            kid = KeyId,
            alg = SecurityAlgorithms.RsaSha256,
            use = "sig",
            n = Base64UrlEncoder.Encode(parameters.Modulus),
            e = Base64UrlEncoder.Encode(parameters.Exponent),
        };
        return JsonSerializer.Serialize(new { keys = new[] { key } });
    }

    /// <summary>An OpenID discovery document whose <c>jwks_uri</c> is <paramref name="jwksUri"/>.</summary>
    public string Discovery(string jwksUri) => JsonSerializer.Serialize(new { issuer = Issuer, jwks_uri = jwksUri });

    /// <summary>A configuration manager that serves this instance's key, for a service that reads its issuer's keys through one (WP12).</summary>
    public IConfigurationManager<OpenIdConnectConfiguration> ConfigurationManager()
    {
        var configuration = new OpenIdConnectConfiguration { Issuer = Issuer };
        configuration.SigningKeys.Add(PublicKey());
        return new StaticConfigurationManager<OpenIdConnectConfiguration>(configuration);
    }

    /// <summary>Makes the service under test trust this instance's key, without a metadata endpoint.</summary>
    public void TrustIn(IServiceCollection services)
    {
        var configuration = new OpenIdConnectConfiguration { Issuer = Issuer };
        configuration.SigningKeys.Add(PublicKey());
        services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
            options.ConfigurationManager = new StaticConfigurationManager<OpenIdConnectConfiguration>(configuration));
    }

    public void Dispose() => _rsa.Dispose();

    private JsonWebKey PublicKey() =>
        JsonWebKeyConverter.ConvertFromRSASecurityKey(new RsaSecurityKey(_rsa.ExportParameters(includePrivateParameters: false)) { KeyId = KeyId });

    private JwtPayload Payload(IEnumerable<Claim>? claims, TimeSpan? expiresIn, string issuer, string audience)
    {
        var given = claims?.ToList() ?? [];
        // A test that names the user (a creator check, for example) gets exactly that subject; any other token gets a fresh one.
        var all = new List<Claim>();
        if (!given.Any(claim => claim.Type == JwtRegisteredClaimNames.Sub))
        {
            all.Add(new Claim(JwtRegisteredClaimNames.Sub, Guid.NewGuid().ToString()));
        }

        all.AddRange(given);
        var expires = _clock.GetUtcNow().UtcDateTime.Add(expiresIn ?? TimeSpan.FromHours(1));
        return new JwtPayload(issuer, audience, all, notBefore: null, expires: expires);
    }

    private static string Write(JwtSecurityToken token) => new JwtSecurityTokenHandler().WriteToken(token);
}
