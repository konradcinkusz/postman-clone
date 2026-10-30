using System.ComponentModel.DataAnnotations;

namespace PostmanClone.Web.Auth;

/// <summary>
/// The <c>Auth</c> configuration section (ADR-0004, docs/AUTH.md). The issuer and audience are authservice's own
/// <c>Jwt:Issuer</c> and <c>Jwt:Audience</c>, which this repository sets to <c>PostmanClone</c>.
/// </summary>
public sealed class AuthServiceOptions
{
    public const string Section = "Auth";

    [Required(AllowEmptyStrings = false)]
    public string Issuer { get; set; } = string.Empty;

    [Required(AllowEmptyStrings = false)]
    public string Audience { get; set; } = string.Empty;

    /// <summary>The OpenID discovery document. Its <c>jwks_uri</c> supplies the keys that verify access tokens.</summary>
    [Required(AllowEmptyStrings = false)]
    public string MetadataAddress { get; set; } = string.Empty;

    /// <summary>Off only for the loopback development stack, the same switch the kernel's JWT validation has.</summary>
    public bool RequireHttpsMetadata { get; set; } = true;
}
