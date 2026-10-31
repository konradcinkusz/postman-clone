using PostmanClone.ServiceDefaults.Authentication;
using PostmanClone.Web.Api;

namespace PostmanClone.Web.Collections;

/// <summary>
/// The rules the shell decides with the page, in one place (BLAZOR-WEB-APP §10: one rule, one home). The service enforces
/// the same rules and stays the boundary; these only keep the page from offering what the service would refuse.
/// </summary>
public static class CollectionRules
{
    /// <summary>
    /// CONTRACTS §2: a collection may be deleted by its creator, or by an Owner or an Admin of the workspace. A Member who
    /// did not create it may not, so the page does not offer the action to them.
    /// </summary>
    public static bool CanDeleteCollection(string? role, Guid createdBy, Guid? userId) =>
        role is WorkspaceRoles.Owner or WorkspaceRoles.Admin || (userId is { } id && id == createdBy);

    /// <summary>
    /// Reads an authservice user id as a Guid for the creator comparison. A value that is not a Guid matches no creator,
    /// so the page offers no creator-only action to it.
    /// </summary>
    public static Guid? UserIdOf(UserInfoResponse? user) =>
        user is not null && Guid.TryParse(user.Id, out var id) ? id : null;
}

/// <summary>What the page says after a call, in plain words. Each text names no id, no path and no server detail.</summary>
public static class CollectionMessages
{
    /// <summary>The text for a failed call. <paramref name="code"/> is the problem's <c>code</c>, when there was one.</summary>
    public static string For(CollectionOutcome outcome, string? code) => (outcome, code) switch
    {
        (CollectionOutcome.Unavailable, _) => "The collections service did not answer. Try again in a moment.",
        (CollectionOutcome.SessionEnded, _) => "Your session has ended. Sign in again to continue.",
        (CollectionOutcome.NoWorkspace, _) => "This account has no single workspace, so there is nothing to show.",
        (CollectionOutcome.NotFound, _) => "That item no longer exists. Someone may have deleted it.",
        (CollectionOutcome.Forbidden, _) => "You do not have permission to make this change.",
        (CollectionOutcome.Conflict, _) => "Not saved: the item changed since you opened it, or a limit was reached (200 collections, 500 folders or 5000 requests per collection).",
        (CollectionOutcome.TooLarge, _) => "The request is too large to save.",
        (CollectionOutcome.RateLimited, _) => "Too many changes in a short time. Wait a moment and try again.",
        (CollectionOutcome.Invalid, "depth_exceeded") => "Folders can be nested eight levels deep at most.",
        (CollectionOutcome.Invalid, "cycle_detected") => "A folder cannot be moved into itself or into one of its own folders.",
        (CollectionOutcome.Invalid, _) => "A name is not valid. Check it and try again.",
        _ => "The change was not saved.",
    };

    public const string CollectionsNotLoaded = "Your collections could not be loaded. Try again in a moment.";

    public const string TreeNotLoaded = "This collection could not be loaded. Expand it again to retry.";

    public const string TabLimit = "Close a tab before opening another. Up to 20 request tabs can be open at once.";

    public const string UnsavedChanges = "This tab has unsaved changes, so it stays open.";

    /// <summary>The note the page shows when the tree had orphans, cycles or duplicates in its data.</summary>
    public static string IssueNote(IReadOnlyList<TreeIssue> issues, string collectionName)
    {
        var moved = issues.Count(issue => issue.Kind is TreeIssueKind.Orphan or TreeIssueKind.Cycle);
        var leftOut = issues.Count(issue => issue.Kind is TreeIssueKind.Duplicate or TreeIssueKind.Foreign);
        var parts = new List<string>();
        if (moved > 0)
        {
            parts.Add($"{moved} item{(moved == 1 ? " is" : "s are")} shown at the top of \"{collectionName}\" because the folder above {(moved == 1 ? "it is" : "them are")} missing or looped");
        }

        if (leftOut > 0)
        {
            parts.Add($"{leftOut} item{(leftOut == 1 ? " was" : "s were")} left out because the data repeats or belongs elsewhere");
        }

        return string.Join("; ", parts) + ".";
    }
}
