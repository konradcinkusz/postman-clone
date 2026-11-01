using System.Text;

namespace PostmanClone.Sending.Assembly;

/// <summary>
/// The encoded body, its content type, and whether that content type replaces a Content-Type row the user wrote.
/// </summary>
public sealed record BodyPlan(byte[]? Content, string? ContentType, bool ReplacesContentType);

/// <summary>
/// Encodes the body (CONTRACTS §1 <c>RequestBody</c>). A raw body gets a default content type for its language; a
/// multipart body always carries the boundary it was built with. Every body is UTF-8 and at most the configured size.
/// </summary>
public static class BodyBuilder
{
    private const string FormUrlEncodedType = "application/x-www-form-urlencoded";

    public static BodyPlan Build(RequestBody body, VariableInterpolator variables, IRandomBytes random, int maxBytes) =>
        body.Type switch
        {
            BodyType.Raw => new BodyPlan(
                Limited(variables.Expand(body.Raw ?? string.Empty), maxBytes),
                RawContentType(body.RawLanguage),
                ReplacesContentType: false),

            BodyType.FormUrlEncoded => new BodyPlan(
                Limited(string.Join('&', Fields(body.Fields, variables).Select(field => $"{FormEncode(field.Key)}={FormEncode(field.Value)}")), maxBytes),
                FormUrlEncodedType,
                ReplacesContentType: false),

            BodyType.FormData => FormData(body, variables, random, maxBytes),

            _ => new BodyPlan(null, null, ReplacesContentType: false),
        };

    private static BodyPlan FormData(RequestBody body, VariableInterpolator variables, IRandomBytes random, int maxBytes)
    {
        var boundary = NewBoundary(random);
        var text = new StringBuilder();
        foreach (var (name, value) in Fields(body.Fields, variables))
        {
            text.Append("--").Append(boundary).Append("\r\n")
                .Append("Content-Disposition: form-data; name=\"").Append(QuoteName(name)).Append("\"\r\n\r\n")
                .Append(value).Append("\r\n");
        }

        text.Append("--").Append(boundary).Append("--\r\n");
        return new BodyPlan(Limited(text.ToString(), maxBytes), $"multipart/form-data; boundary={boundary}", ReplacesContentType: true);
    }

    /// <summary>Enabled rows with a non-empty key template, expanded and checked for well-formed text.</summary>
    private static List<KeyValuePair<string, string>> Fields(IEnumerable<KeyValueRow> rows, VariableInterpolator variables) =>
        rows
            .Where(row => row.Enabled && row.Key.Length > 0)
            .Select(row => new KeyValuePair<string, string>(
                AssemblyText.RequireWellFormed(variables.Expand(row.Key), "form field name"),
                AssemblyText.RequireWellFormed(variables.Expand(row.Value), "form field value")))
            .ToList();

    private static byte[] Limited(string text, int maxBytes)
    {
        var bytes = AssemblyText.Utf8(text, "body");
        if (bytes.Length > maxBytes)
        {
            throw new RequestAssemblyException(RequestAssemblyException.BodyTooLarge, "The request body is larger than the sender allows.");
        }

        return bytes;
    }

    /// <summary>
    /// The default for each language (CONTRACTS §1). A raw body without a language sends no default content type.
    /// </summary>
    private static string? RawContentType(RawLanguage? language) => language switch
    {
        RawLanguage.Json => "application/json",
        RawLanguage.Text => "text/plain",
        RawLanguage.Xml => "application/xml",
        RawLanguage.Html => "text/html",
        RawLanguage.Javascript => "application/javascript",
        _ => null,
    };

    /// <summary>16 bytes from the injected source: 128 bits, so a boundary cannot be guessed from a field value.</summary>
    private static string NewBoundary(IRandomBytes random)
    {
        Span<byte> bytes = stackalloc byte[16];
        random.Fill(bytes);
        return "----PostmanClone" + Convert.ToHexStringLower(bytes);
    }

    /// <summary>Form encoding: percent-encoding with a space written as <c>+</c>.</summary>
    private static string FormEncode(string value) => Uri.EscapeDataString(value).Replace("%20", "+", StringComparison.Ordinal);

    /// <summary>A field name sits inside a quoted header parameter, so quotes and line breaks are percent-encoded.</summary>
    private static string QuoteName(string name) =>
        name.Replace("\"", "%22", StringComparison.Ordinal)
            .Replace("\r", "%0D", StringComparison.Ordinal)
            .Replace("\n", "%0A", StringComparison.Ordinal);
}
