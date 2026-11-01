namespace PostmanClone.Sending.Limits;

/// <summary>
/// The caps the shared sender applies (CONTRACTS §4). The sender never widens them: a host may tighten the values it
/// binds, and the agent uses the same defaults as ExecutionService so a result is the same whichever sender answered.
/// </summary>
/// <param name="RequestBodyMaxBytes">The raw request body, in bytes.</param>
/// <param name="MaxHeaders">Headers and query rows, in total.</param>
/// <param name="UrlMaxLength">The URL, in characters, after expansion.</param>
/// <param name="ResponseReadCapBytes">Bytes read from a response before it is cut off and marked truncated.</param>
/// <param name="RefuseDeclaredLengthBytes">A declared Content-Length above this is refused without reading the body.</param>
/// <param name="DefaultTimeoutSeconds">The send deadline when the request names none.</param>
/// <param name="MaxTimeoutSeconds">The largest deadline a request may name.</param>
/// <param name="MaxRedirects">Redirect hops followed in one send.</param>
public sealed record SendingLimits(
    int RequestBodyMaxBytes,
    int MaxHeaders,
    int UrlMaxLength,
    long ResponseReadCapBytes,
    long RefuseDeclaredLengthBytes,
    int DefaultTimeoutSeconds,
    int MaxTimeoutSeconds,
    int MaxRedirects)
{
    /// <summary>The defaults of CONTRACTS §4.</summary>
    public static SendingLimits Contract { get; } = new(
        RequestBodyMaxBytes: 1_048_576,
        MaxHeaders: 100,
        UrlMaxLength: 8192,
        ResponseReadCapBytes: 5 * 1024 * 1024,
        RefuseDeclaredLengthBytes: 50 * 1024 * 1024,
        DefaultTimeoutSeconds: 30,
        MaxTimeoutSeconds: 60,
        MaxRedirects: 5);
}
