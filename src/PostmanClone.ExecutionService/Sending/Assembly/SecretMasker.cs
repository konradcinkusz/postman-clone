using System.Text;

namespace PostmanClone.ExecutionService.Sending.Assembly;

/// <summary>
/// Hides secrets and sensitive names in what is shown, stored or logged (CONTRACTS §4, ADR-0006, ADR-0012).
/// A secret is masked in every form it can take in the text: as written, percent-encoded, form-encoded (a space as
/// <c>+</c>) and base64. The longest form is replaced first, so a short form inside a longer one cannot break the
/// longer match. Header values and query values whose name is sensitive are replaced whole.
/// </summary>
/// <remarks>
/// A secret that contains the mask character itself cannot be guaranteed hidden. Such a value is not expected from a
/// user and is not special-cased here.
/// </remarks>
public sealed class SecretMasker
{
    public const string Mask = "••••••";

    private const int MinimumReliableLength = 4;

    private static readonly string[] SensitiveNames = ["Authorization", "Proxy-Authorization", "Cookie", "Set-Cookie"];

    private static readonly string[] SensitiveFragments = ["token", "secret", "password", "apikey", "api-key", "api_key"];

    private readonly IReadOnlyList<string> _forms;

    private SecretMasker(IReadOnlyList<string> forms, IReadOnlyList<AssemblyWarning> warnings)
    {
        _forms = forms;
        Warnings = warnings;
    }

    /// <summary>Non-fatal findings: short secrets, whose masking can hide unrelated text.</summary>
    public IReadOnlyList<AssemblyWarning> Warnings { get; }

    /// <summary>Builds the masker for the secret values of one request. Non-secret values are not masked.</summary>
    public static SecretMasker For(IEnumerable<ResolvedValue> values)
    {
        var secrets = values
            .Where(value => value.IsSecret && value.Value.Length > 0)
            .Select(value => value.Value)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        var forms = secrets
            .SelectMany(Forms)
            .Distinct(StringComparer.Ordinal)
            .OrderByDescending(form => form.Length)
            .ToList();

        var warnings = new List<AssemblyWarning>();
        var shortCount = secrets.Count(secret => secret.Length < MinimumReliableLength);
        if (shortCount > 0)
        {
            warnings.Add(new AssemblyWarning(
                "secret_short",
                $"{shortCount} secret value(s) are shorter than {MinimumReliableLength} characters; every occurrence is masked, which can hide unrelated text."));
        }

        return new SecretMasker(forms, warnings);
    }

    /// <summary>True for a header or query name whose value is never shown (CONTRACTS §4).</summary>
    public static bool IsSensitiveName(string name)
    {
        if (SensitiveNames.Any(sensitive => string.Equals(sensitive, name, StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        return SensitiveFragments.Any(fragment => name.Contains(fragment, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Removes the longest tail of <paramref name="text"/> that is the beginning of a secret form. A body cut at the read cap
    /// can end inside a secret: the part before the cut would show, unmasked, because the rest is not there to match (OI-12).
    /// Only a proper prefix is removed, so a whole secret at the end is left for <see cref="MaskText"/>.
    /// </summary>
    public string TrimPartialSecretAtEnd(string text)
    {
        var longest = _forms.Count == 0 ? 0 : _forms[0].Length;
        for (var length = Math.Min(longest - 1, text.Length); length > 0; length--)
        {
            var tail = text[^length..];
            if (_forms.Any(form => form.Length > length && form.StartsWith(tail, StringComparison.Ordinal)))
            {
                return text[..^length];
            }
        }

        return text;
    }

    /// <summary>Masks every secret form in free text, such as a body.</summary>
    public string MaskText(string text)
    {
        foreach (var form in _forms)
        {
            text = text.Replace(form, Mask, StringComparison.Ordinal);
        }

        return text;
    }

    /// <summary>Masks a URL: the value of a sensitive query name, then every secret form.</summary>
    public string MaskUrl(string url)
    {
        var question = url.IndexOf('?');
        if (question < 0)
        {
            return MaskText(url);
        }

        var pairs = url[(question + 1)..].Split('&');
        for (var index = 0; index < pairs.Length; index++)
        {
            var equals = pairs[index].IndexOf('=');
            if (equals > 0 && IsSensitiveName(Uri.UnescapeDataString(pairs[index][..equals])))
            {
                pairs[index] = $"{pairs[index][..(equals + 1)]}{Mask}";
            }
        }

        return MaskText(url[..(question + 1)] + string.Join('&', pairs));
    }

    /// <summary>Masks header names and values. A sensitive name has its whole value replaced.</summary>
    public IReadOnlyList<HeaderLine> MaskHeaders(IEnumerable<HeaderLine> headers) =>
        headers
            .Select(header => new HeaderLine(
                MaskText(header.Name),
                IsSensitiveName(header.Name) ? Mask : MaskText(header.Value)))
            .ToList();

    private static IEnumerable<string> Forms(string secret)
    {
        var percentEncoded = Uri.EscapeDataString(secret);
        yield return secret;
        yield return percentEncoded;
        yield return percentEncoded.Replace("%20", "+", StringComparison.Ordinal);
        yield return Convert.ToBase64String(Encoding.UTF8.GetBytes(secret));
    }
}
