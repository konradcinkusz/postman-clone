namespace PostmanClone.Sending.Assembly;

/// <summary>
/// A request that cannot be turned into a concrete request. <see cref="Code"/> is stable and snake_case; the message
/// names a field and never carries a value, because a value may be a secret (ADR-0012).
/// </summary>
public sealed class RequestAssemblyException(string code, string message, Exception? innerException = null)
    : Exception(message, innerException)
{
    public const string InvalidMethod = "invalid_method";
    public const string InvalidUrl = "invalid_url";
    public const string UrlTooLong = "url_too_long";
    public const string InvalidText = "invalid_text";
    public const string InvalidHeaderName = "invalid_header_name";
    public const string InvalidHeaderValue = "invalid_header_value";
    public const string ForbiddenHeader = "forbidden_header";
    public const string TooManyHeaders = "too_many_headers";
    public const string BodyTooLarge = "body_too_large";
    public const string SecretLiteralNotAllowed = "secret_literal_not_allowed";
    public const string InvalidAuth = "invalid_auth";

    /// <summary>The stable code, one of the constants on this type.</summary>
    public string Code { get; } = code;
}
