namespace PostmanClone.ExecutionService.Sending.Ssrf;

/// <summary>
/// A request target that passed the URL checks. <see cref="Uri"/> is the canonical form the sender must send: the host
/// is lower-cased, punycoded, or written as the address it parses to, so the text sent and the text checked agree.
/// </summary>
/// <param name="Scheme">Always <c>http</c> or <c>https</c>.</param>
/// <param name="Host">The host without brackets: an ASCII name, or the address text.</param>
/// <param name="Port">The explicit or default port, 1 to 65535.</param>
/// <param name="Uri">The absolute URL to send, without a fragment.</param>
/// <param name="IsLiteral">True when the host is an IP address rather than a name.</param>
public sealed record SsrfTarget(string Scheme, string Host, int Port, string Uri, bool IsLiteral);
