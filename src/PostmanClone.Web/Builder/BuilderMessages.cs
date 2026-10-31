using PostmanClone.Web.Api;
using PostmanClone.Web.Collections;

namespace PostmanClone.Web.Builder;

/// <summary>
/// What the builder says, in plain words (BLAZOR-WEB-APP §10: one place for the texts). None names a value the user typed,
/// a header value or a secret, so a message can be shown, copied or logged without leaking one.
/// </summary>
public static class BuilderMessages
{
    public const string SecretLiteral = "Use a {{variable}} reference, not the value itself. Secret values are kept in an environment, never in a request.";

    public const string Unsaved = "This request has unsaved changes.";

    public const string Saved = "Saved.";

    public const string LoadFailed = "This request could not be loaded. Try again in a moment.";

    public const string SaveFailed = "Not saved. Try again in a moment.";

    public const string SaveConflict = "This request was changed by someone else since you opened it. Reload it to take their version, or overwrite it with yours.";

    public const string SaveRefused = "Not saved: some fields are not valid. Check the fields marked in red.";

    public const string SaveTooLarge = "Not saved: the request is too large. Shorten the URL, the body or the number of rows.";

    public const string SizeNote = "This request is larger than the service keeps (1.5 MB as JSON). Shorten the body to save it.";

    public const string EmptyBody = "The body is empty, so there is nothing to beautify.";

    public const string BulkCrossCheck = "A name with a colon cannot be written in bulk mode. Edit that row in the table instead.";

    public const string ReservedHeader = "This header is often set by the service or the auth helper. Check that it does not conflict.";

    public const string ReferenceOnlyHint = "Enter a variable such as {{token}}. A literal value is refused, so it is never stored.";

    public const string UnresolvedNote = "The variable is not in the selected environment, so it is sent as written.";

    public const string UntitledName = "Untitled request";

    /// <summary>The default URL of a new request. It is not a host, so nothing is sent until the sender exists.</summary>
    public const string NewRequestUrl = "https://";

    /// <summary>The line the page shows for a refused save, from the service's answer (CONTRACTS §0 codes).</summary>
    public static string ForSave(CollectionOutcome outcome, string? code) => (outcome, code) switch
    {
        (CollectionOutcome.Conflict, _) => SaveConflict,
        (CollectionOutcome.TooLarge, _) => SaveTooLarge,
        (CollectionOutcome.Invalid, "secret_literal_not_allowed") => $"Not saved: a secret field holds a literal value. {SecretLiteral}",
        (CollectionOutcome.Invalid, _) => SaveRefused,
        (CollectionOutcome.Unavailable or CollectionOutcome.NotFound or CollectionOutcome.Forbidden or CollectionOutcome.RateLimited
            or CollectionOutcome.SessionEnded or CollectionOutcome.NoWorkspace, _) => CollectionMessages.For(outcome, code),
        _ => SaveFailed,
    };

    /// <summary>A field name the page can show next to its input, from the service's field path.</summary>
    public static string ForField(string field) => field switch
    {
        "request.url" => "URL",
        "request.method" => "Method",
        "request.body.type" => "Body type",
        "request.body.rawLanguage" => "Raw language",
        "request.body.raw" => "Raw body",
        "request.body.fields" => "Form fields",
        "request.auth.type" => "Auth type",
        "request.auth.bearerToken" => "Bearer token",
        "request.auth.password" => "Password",
        "request.auth.apiKeyValue" => "API key value",
        "request.auth.apiKeyIn" => "API key location",
        _ => field,
    };
}
