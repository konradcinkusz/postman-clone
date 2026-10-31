namespace PostmanClone.Web.Builder;

/// <summary>
/// The advice the header editor shows (CONTRACTS §4, WP19). A header the service or an auth helper usually sets, or one whose name
/// suggests a credential, gets a note. It is advice only: the rules are the validator's, and a header is never refused for its name.
/// </summary>
public static class HeaderAdvice
{
    private static readonly string[] Reserved = ["authorization", "proxy-authorization", "cookie", "set-cookie", "x-api-key", "x-auth-token"];

    public static bool IsReserved(string name)
    {
        var lower = name.Trim().ToLowerInvariant();
        return Array.IndexOf(Reserved, lower) >= 0
            || lower.Contains("token", StringComparison.Ordinal)
            || lower.Contains("secret", StringComparison.Ordinal)
            || lower.Contains("password", StringComparison.Ordinal)
            || lower.Contains("apikey", StringComparison.Ordinal)
            || lower.Contains("api-key", StringComparison.Ordinal);
    }
}
