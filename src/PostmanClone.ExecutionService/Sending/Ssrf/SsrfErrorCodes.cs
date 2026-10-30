namespace PostmanClone.ExecutionService.Sending.Ssrf;

/// <summary>
/// The codes a refused target carries. They are the sender's <c>errorCode</c> values (CONTRACTS §4), so the sender
/// can pass them through without translating.
/// </summary>
public static class SsrfErrorCodes
{
    public const string InvalidUrl = "invalid_url";
    public const string BlockedScheme = "ssrf_blocked_scheme";
    public const string BlockedPort = "ssrf_blocked_port";
    public const string BlockedAddress = "ssrf_blocked_address";
    public const string DnsFailed = "dns_failed";
    public const string TooManyRedirects = "too_many_redirects";
}
