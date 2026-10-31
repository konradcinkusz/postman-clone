using PostmanClone.Web.Api;

namespace PostmanClone.Web.Environments;

/// <summary>
/// What the environments page says, in plain words (BLAZOR-WEB-APP §10: one place for the texts). None of them names a value
/// the user typed or a stored one, so a message can be shown or logged without leaking a secret.
/// </summary>
public static class EnvironmentMessages
{
    public const string NameRequired = "Enter a name with at least one character that is not a space.";

    public const string NameTooLong = "A name is at most 100 characters.";

    public const string KeyRequired = "Enter a key.";

    public const string KeyTooLong = "A key is at most 100 characters.";

    public const string KeyShape = "A key may use only letters, digits, '_', '.' and '-'.";

    public const string KeyDuplicate = "Another variable in this environment has this key. Keys must be unique.";

    public const string ValueTooLong = "A value is at most 8192 characters.";

    public const string VariableLimit = "An environment holds at most 200 variables. Remove some before adding more.";

    public const string SecretNeedsValue = "Type the secret's value. A stored secret's value is never shown, so it is entered again to change it.";

    public const string SecretRenamed = "This secret was stored under another key. Type its value again, because it is encrypted for its key.";

    public const string PlainNeedsValue = "Type a value. A secret's value is not shown, so it must be entered again when the variable becomes plain.";

    public const string ServerRowFallback = "The service refused this variable. Check its key, value and secret setting.";

    public const string SecretStored = "stored";

    public const string SecretNoValue = "no value";

    public const string SecretPlaceholderStored = "••••••";

    public const string SecretPlaceholderEmpty = "Type a value";

    public const string SecretsUnavailable =
        "Secret variables are unavailable: the service has no encryption key. Plain variables still work, but a secret value cannot be saved.";

    public const string LimitReached =
        "This workspace already has the maximum of 50 environments. Delete one to create another.";

    public const string NotFound = "This environment no longer exists. It may have been deleted, or it belongs to another workspace.";

    public const string DeleteForbidden = "Only the creator, an Admin or an Owner can delete this environment.";

    public const string SaveFailed = "Not saved. Try again in a moment.";

    public const string SaveRefused = "Not saved: some fields are not valid. Check the fields marked in red.";

    public const string SaveDuplicate = "Not saved: two variables have the same key. Keys must be unique.";

    public const string ListFailed = "Your environments could not be loaded. Try again in a moment.";

    public const string Created = "Environment created.";

    public const string Saved = "Saved.";

    public const string Deleted = "Environment deleted.";

    public const string Unsaved = "This environment has unsaved changes.";

    public const string DeleteConfirmation = "Delete this environment and its variables? This cannot be undone.";

    public const string Hint =
        "Put credentials here as secret variables, then reference them in a request as {{name}}. A secret's value is encrypted and never shown again.";

    /// <summary>The line for a failed call, from the outcome and the service's code.</summary>
    public static string ForFailure(CollectionOutcome outcome, string? code) => (outcome, code) switch
    {
        (CollectionOutcome.Unavailable, "secrets_unavailable") => SecretsUnavailable,
        (CollectionOutcome.Unavailable, _) => "The environments service did not answer. Try again in a moment.",
        (CollectionOutcome.NotFound, _) => NotFound,
        (CollectionOutcome.Forbidden, _) => DeleteForbidden,
        (CollectionOutcome.Conflict, _) => SaveDuplicate,
        (CollectionOutcome.Invalid, _) => SaveRefused,
        (CollectionOutcome.RateLimited, _) => "Too many changes in a short time. Wait a moment and try again.",
        (CollectionOutcome.SessionEnded, _) => "Your session has ended. Sign in again to continue.",
        (CollectionOutcome.NoWorkspace, _) => "This account has no single workspace, so there is nothing to show.",
        _ => SaveFailed,
    };
}
