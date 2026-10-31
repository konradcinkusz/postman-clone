using System.Text.RegularExpressions;
using PostmanClone.CollectionsService.Domain;

namespace PostmanClone.CollectionsService.Interop.Postman;

/// <summary>
/// Which values an import must not keep (ADR-0016, ADR-0006). A name is sensitive when CONTRACTS §4 masks it, or when it
/// contains one of the secret words. A value is a literal when it is not a <c>{{name}}</c> reference, so a literal
/// secret never reaches the stored definition.
/// </summary>
internal static class SecretRules
{
    private static readonly HashSet<string> SensitiveNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Authorization",
        "Proxy-Authorization",
        "Cookie",
        "Set-Cookie",
        "X-Api-Key",
        "X-Auth-Token",
    };

    private static readonly string[] SensitiveWords = ["token", "secret", "password", "apikey", "api-key", "api_key"];

    /// <summary>A reference anywhere in a text, such as the <c>{{token}}</c> in <c>Bearer {{token}}</c>.</summary>
    private static readonly Regex ReferenceAnywhere = new(
        @"\{\{[A-Za-z0-9_.\-]{1,100}\}\}",
        RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(1));

    public static bool IsSensitive(string name)
    {
        var trimmed = name.Trim();
        if (SensitiveNames.Contains(trimmed))
        {
            return true;
        }

        var lower = trimmed.ToLowerInvariant();
        return SensitiveWords.Any(word => lower.Contains(word, StringComparison.Ordinal));
    }

    /// <summary>
    /// A row value (a header, a query parameter, a form field) is a literal when it has text and no reference in it. A value
    /// that already names a variable is a template, and it is kept.
    /// </summary>
    public static bool IsLiteralRowValue(string value) => value.Trim().Length > 0 && !ReferenceAnywhere.IsMatch(value);

    /// <summary>An auth-helper value is a literal unless it is exactly one reference (ADR-0006, CONTRACTS §1).</summary>
    public static bool IsLiteralAuthValue(string value) => !RequestDefinitionValidator.IsSecretReference(value);

    /// <summary>The <c>{{name}}</c> text a generated variable takes in a field.</summary>
    public static string Reference(string variable) => "{{" + variable + "}}";
}
