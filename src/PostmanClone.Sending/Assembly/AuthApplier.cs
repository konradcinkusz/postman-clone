using System.Text.RegularExpressions;

namespace PostmanClone.Sending.Assembly;

/// <summary>What the auth helper adds to a request: header lines, query pairs, and the warnings it raised.</summary>
public sealed record AuthEffects(
    IReadOnlyList<HeaderLine> Headers,
    IReadOnlyList<KeyValuePair<string, string>> Query,
    IReadOnlyList<AssemblyWarning> Warnings);

/// <summary>
/// Applies the auth helper (CONTRACTS §1 <c>RequestAuth</c>). The helper's header replaces a header row of the same name,
/// and the replacement is reported in a warning. The secret fields accept a <c>{{name}}</c> reference only (ADR-0006).
/// </summary>
public static partial class AuthApplier
{
    private const string OverrideCode = "auth_overrides_header";

    public static AuthEffects Apply(RequestAuth auth, VariableInterpolator variables, IReadOnlyList<HeaderLine> headers)
    {
        var result = headers.ToList();
        var query = new List<KeyValuePair<string, string>>();
        var warnings = new List<AssemblyWarning>();

        switch (auth.Type)
        {
            case AuthType.Bearer:
                var token = Reference(auth.BearerToken, variables, "bearerToken");
                if (token is not null)
                {
                    SetHeader(result, HeaderBuilder.Checked("Authorization", $"Bearer {token}"), warnings);
                }

                break;

            case AuthType.Basic:
                var username = variables.Expand(auth.Username ?? string.Empty);
                var password = Reference(auth.Password, variables, "password") ?? string.Empty;
                if (username.Length > 0 || password.Length > 0)
                {
                    if (username.Contains(':'))
                    {
                        throw new RequestAssemblyException(RequestAssemblyException.InvalidAuth, "The Basic username cannot contain a colon.");
                    }

                    var credentials = Convert.ToBase64String(AssemblyText.Utf8($"{username}:{password}", "credentials"));
                    SetHeader(result, HeaderBuilder.Checked("Authorization", $"Basic {credentials}"), warnings);
                }

                break;

            case AuthType.ApiKey:
                var keyName = auth.ApiKeyName;
                if (!string.IsNullOrEmpty(keyName))
                {
                    var keyValue = Reference(auth.ApiKeyValue, variables, "apiKeyValue") ?? string.Empty;
                    if (auth.ApiKeyIn == ApiKeyLocation.Query)
                    {
                        query.Add(new KeyValuePair<string, string>(keyName, keyValue));
                    }
                    else
                    {
                        SetHeader(result, HeaderBuilder.Checked(keyName, keyValue), warnings);
                    }
                }

                break;
        }

        return new AuthEffects(result, query, warnings);
    }

    /// <summary>
    /// The expanded value of a secret field, or null when the field is empty. A literal value (anything that is not
    /// exactly one reference) is refused with <c>secret_literal_not_allowed</c>, and its text is never echoed.
    /// </summary>
    private static string? Reference(string? field, VariableInterpolator variables, string fieldName)
    {
        if (string.IsNullOrEmpty(field))
        {
            return null;
        }

        if (!WholeReference().IsMatch(field))
        {
            throw new RequestAssemblyException(
                RequestAssemblyException.SecretLiteralNotAllowed,
                $"The {fieldName} field must be a variable reference, not a literal value.");
        }

        return variables.Expand(field.Trim());
    }

    private static void SetHeader(List<HeaderLine> headers, HeaderLine line, List<AssemblyWarning> warnings)
    {
        if (headers.RemoveAll(header => HeaderBuilder.SameName(header.Name, line.Name)) > 0)
        {
            warnings.Add(new AssemblyWarning(OverrideCode, "The auth helper replaced a header row with the same name."));
        }

        headers.Add(line);
    }

    [GeneratedRegex(@"^\s*\{\{[A-Za-z0-9_.\-]{1,100}\}\}\s*$", RegexOptions.CultureInvariant)]
    private static partial Regex WholeReference();
}
