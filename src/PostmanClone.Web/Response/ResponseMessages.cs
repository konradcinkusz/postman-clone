using PostmanClone.Web.Api;
using PostmanClone.Web.Builder;

namespace PostmanClone.Web.Response;

/// <summary>
/// What the response viewer and the send say, in plain words (BLAZOR-WEB-APP §10). Each refusal has its own text. A code this page
/// does not know gets a text that names the category only, never a guess, and never the word "failed" on its own.
/// None of these texts carries an address, a header value, a variable or a secret.
/// </summary>
public static class ResponseMessages
{
    public const string Preview = "Preview is not available. HTML from a response is never rendered here, so a page cannot run its scripts in this tool.";

    public const string BinaryNote = "This is binary data, so it is not shown as text. Its size is shown above.";

    public const string Truncated = "The body was cut at the size limit (5 MB). Only the part shown was kept.";

    public const string NotJson = "This body is not valid JSON, so it is shown as raw text.";

    public const string SlowDown = "You are sending too fast. Wait a minute and try again.";

    public const string Cancelled = "The send was cancelled. Nothing was kept.";

    public static (string Heading, string Explanation) ForOutcome(OutcomeKind outcome, string? errorCode) => outcome switch
    {
        OutcomeKind.Completed => ("Response received", string.Empty),
        OutcomeKind.Blocked => (
            "Request blocked",
            errorCode switch
            {
                "ssrf_blocked_address" => "The sender does not reach private, loopback or link-local addresses, so this target was refused.",
                "ssrf_blocked_scheme" => "The sender only sends http and https requests.",
                "ssrf_blocked_port" => "The sender refuses this port, which belongs to a database or a mail service.",
                "dns_failed" => "The host name could not be resolved, so nothing was sent.",
                "too_many_redirects" => "The redirect chain is longer than the sender follows, so it stopped.",
                "invalid_url" => "The URL is not one the sender can send. Check the scheme and the host.",
                _ => "The sender refused this request.",
            }),
        OutcomeKind.Timeout => ("Timed out", "The target did not finish answering within the time limit, so the send stopped."),
        OutcomeKind.NetworkError => (
            "Network error",
            errorCode switch
            {
                "invalid_body_encoding" => "The response body could not be decoded, so it was not kept.",
                _ => "The target could not be reached, or it stopped answering part way through.",
            }),
        OutcomeKind.InvalidRequest => (
            "Request not valid",
            "The sender could not build a request from this definition. Check the URL, the headers and the body."),
        OutcomeKind.TooLarge => ("Response too large", "The response declares a size the sender refuses to read."),
        _ => ("Unexpected result", "The sender returned a result this page does not recognise. Try the request again."),
    };

    /// <summary>The text for a send the sender never answered (no response to show).</summary>
    public static string ForFailure(CollectionOutcome outcome, string? code) => (outcome, code) switch
    {
        (CollectionOutcome.NotFound, _) => "The selected environment could not be found. Choose another one, or none.",
        (CollectionOutcome.Forbidden, "no_active_workspace") => "Your sign-in does not name a workspace, so nothing can be sent.",
        (CollectionOutcome.Forbidden, "multiple_workspaces") => "Your sign-in belongs to more than one organization, so the sender refuses to choose one.",
        (CollectionOutcome.Forbidden, _) => "Your role does not allow this here.",
        (CollectionOutcome.Invalid, "secret_literal_not_allowed") => BuilderMessages.SecretLiteral,
        (CollectionOutcome.Invalid, _) => "The sender refused the request as not valid. Check the fields marked in the builder.",
        (CollectionOutcome.TooLarge, _) => "The request is larger than the sender accepts. Shorten the body.",
        (CollectionOutcome.RateLimited, _) => SlowDown,
        (CollectionOutcome.SessionEnded, _) => "Your session has ended. Sign in again to send requests.",
        (CollectionOutcome.NoWorkspace, _) => "Your sign-in has no workspace, so nothing can be sent.",
        (CollectionOutcome.Unavailable, _) => "The sender could not be reached, or the environments service failed. Try again in a moment.",
        _ => $"The sender refused the request{(code is null ? string.Empty : $" ({code})")}.",
    };
}
