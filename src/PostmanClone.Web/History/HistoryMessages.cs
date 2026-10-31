using PostmanClone.Web.Api;

namespace PostmanClone.Web.History;

/// <summary>The sentences of the history pages and panel (WP22). Every one is written as text; none names an internal address.</summary>
public static class HistoryMessages
{
    public const string Empty = "Nothing has been sent in this workspace yet. Send a request and it appears here.";

    public const string EmptyFiltered = "No entries match this filter.";

    public const string LoadFailed = "The history could not be loaded. Try again in a moment.";

    public const string NotFound = "This entry is not in your history. It may have been deleted.";

    public const string SessionEnded = "Your sign-in has ended. Sign in again to see your history.";

    public const string NoWorkspace = "Your sign-in does not name one workspace, so there is no history to show.";

    public const string MaskNote = "Secret values were masked when this request was sent and are not stored, so they show as ••••••. The request keeps its {{variable}} references.";

    public const string MaskedBeforeSend = "This request still holds masked values (••••••). Enter the real value, or a {{variable}} reference, before you send or save it.";

    public const string Reopened = "Opened from history as a new request. It is not saved until you save it, and no secret is restored.";

    public const string ConfirmDelete = "Delete this entry? It cannot be undone.";

    public const string ConfirmClear = "Clear your history in this workspace? Only your entries are removed, and this cannot be undone.";

    public const string Deleted = "The entry was deleted.";

    public const string Cleared = "Your history in this workspace was cleared.";

    /// <summary>What a history call's failure says, in the terms the page acts on.</summary>
    public static string ForFailure(CollectionOutcome outcome) => outcome switch
    {
        CollectionOutcome.NotFound => NotFound,
        CollectionOutcome.SessionEnded => SessionEnded,
        CollectionOutcome.NoWorkspace => NoWorkspace,
        _ => LoadFailed,
    };
}
