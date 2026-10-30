using PostmanClone.ExecutionService.Options;

namespace PostmanClone.ExecutionService.Sending.Ssrf;

/// <summary>
/// Decides each redirect hop. Redirects are followed by the sender, never by the HTTP stack, so every hop goes
/// through the same URL checks as the first request and the connection guard. A hop is refused when the chain is
/// too long, when the target fails the URL checks, or when an https request would be sent back over plain http.
/// On a cross-origin hop the sender drops the headers that <see cref="ShouldDropHeader"/> names.
/// </summary>
public sealed class RedirectPlanner(ExecutionOptions options)
{
    /// <summary>
    /// Headers that carry credentials (the set CONTRACTS §4 masks) and must not follow the request to another origin.
    /// </summary>
    private static readonly string[] SensitiveHeaders =
        ["Authorization", "Proxy-Authorization", "Cookie", "X-Api-Key", "X-Auth-Token"];

    private readonly TargetValidator _validator = new(options);

    /// <param name="current">The target that produced the response.</param>
    /// <param name="method">The method of the request that produced the response.</param>
    /// <param name="statusCode">The response status code.</param>
    /// <param name="location">The Location header, or null when the response has none.</param>
    /// <param name="redirectsFollowed">Hops already followed before this response.</param>
    public RedirectStep Plan(SsrfTarget current, string method, int statusCode, string? location, int redirectsFollowed)
    {
        if (!IsRedirectStatus(statusCode) || string.IsNullOrWhiteSpace(location))
        {
            return RedirectStep.NotRedirect;
        }

        if (redirectsFollowed >= options.MaxRedirects)
        {
            return RedirectStep.Refuse(SsrfErrorCodes.TooManyRedirects);
        }

        if (!TryResolve(current, location, out var absolute))
        {
            return RedirectStep.Refuse(SsrfErrorCodes.InvalidUrl);
        }

        var check = _validator.Validate(absolute);
        if (!check.Accepted)
        {
            return RedirectStep.Refuse(check.ErrorCode!);
        }

        var next = check.Target!;
        if (current.Scheme == "https" && next.Scheme == "http")
        {
            return RedirectStep.Refuse(SsrfErrorCodes.BlockedScheme);
        }

        var crossOrigin = current.Scheme != next.Scheme
            || current.Port != next.Port
            || !string.Equals(current.Host, next.Host, StringComparison.OrdinalIgnoreCase);

        var (nextMethod, dropBody) = MethodAfter(statusCode, method);
        return new RedirectStep(RedirectOutcome.Follow, next, nextMethod, dropBody, crossOrigin, null);
    }

    /// <summary>True when the header must be dropped for this hop.</summary>
    public static bool ShouldDropHeader(string headerName, RedirectStep step) =>
        step.CrossOrigin && SensitiveHeaders.Contains(headerName, StringComparer.OrdinalIgnoreCase);

    private static bool IsRedirectStatus(int statusCode) => statusCode is 301 or 302 or 303 or 307 or 308;

    /// <summary>
    /// 303 becomes GET (HEAD stays HEAD). 301 and 302 turn a POST into GET, as browsers do. 307 and 308 keep the method and
    /// the body. Whenever the method changes, the body is dropped.
    /// </summary>
    private static (string Method, bool DropBody) MethodAfter(int statusCode, string method)
    {
        var next = statusCode switch
        {
            303 when method != "HEAD" => "GET",
            301 or 302 when method == "POST" => "GET",
            _ => method,
        };

        return (next, next != method);
    }

    private static bool TryResolve(SsrfTarget current, string location, out string absolute)
    {
        absolute = string.Empty;
        try
        {
            absolute = new Uri(new Uri(current.Uri, UriKind.Absolute), location).AbsoluteUri;
            return true;
        }
        catch (UriFormatException)
        {
            return false;
        }
    }
}
