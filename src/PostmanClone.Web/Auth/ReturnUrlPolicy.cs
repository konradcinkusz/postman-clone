namespace PostmanClone.Web.Auth;

/// <summary>
/// Makes a <c>ReturnUrl</c> safe to redirect to (FRONTEND-BFF §4, ADR-0004). Only a local path is kept: one leading
/// slash, no second slash or backslash that a browser would read as a host, no control characters. Anything else, and
/// the sign-in page itself, falls back to home.
/// </summary>
public static class ReturnUrlPolicy
{
    public const string Fallback = "/";
    private const int MaximumLength = 2048;

    public static string Sanitize(string? returnUrl)
    {
        if (string.IsNullOrEmpty(returnUrl) || returnUrl.Length > MaximumLength || returnUrl[0] != '/')
        {
            return Fallback;
        }

        // "//host" and "/\host" are protocol-relative to another site in a browser.
        if (returnUrl.Length > 1 && (returnUrl[1] is '/' or '\\'))
        {
            return Fallback;
        }

        foreach (var character in returnUrl)
        {
            if (character == '\\' || char.IsControl(character))
            {
                return Fallback;
            }
        }

        return returnUrl.StartsWith("/sign-in", StringComparison.OrdinalIgnoreCase) ? Fallback : returnUrl;
    }

    /// <summary>The sign-in address for a return path, with the error code if there is one. Only codes, never tokens, go in a URL.</summary>
    public static string SignInPath(string? returnUrl, string? error = null)
    {
        var sanitized = Uri.EscapeDataString(Sanitize(returnUrl));
        return error is null
            ? $"/sign-in?ReturnUrl={sanitized}"
            : $"/sign-in?error={error}&ReturnUrl={sanitized}";
    }
}
