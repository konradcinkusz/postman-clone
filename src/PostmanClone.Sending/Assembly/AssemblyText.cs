using System.Text;

namespace PostmanClone.Sending.Assembly;

/// <summary>
/// The text rules every assembled string obeys: well-formed UTF-16 (a lone surrogate cannot be encoded), so nothing
/// that reaches the wire or a percent-encoder is malformed.
/// </summary>
internal static class AssemblyText
{
    private static readonly UTF8Encoding Strict = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    /// <summary>Returns the text when it is well-formed, otherwise throws <c>invalid_text</c>.</summary>
    public static string RequireWellFormed(string value, string field)
    {
        _ = Utf8(value, field);
        return value;
    }

    public static byte[] Utf8(string value, string field)
    {
        try
        {
            return Strict.GetBytes(value);
        }
        catch (EncoderFallbackException exception)
        {
            throw new RequestAssemblyException(RequestAssemblyException.InvalidText, $"The {field} is not well-formed Unicode text.", exception);
        }
    }
}
