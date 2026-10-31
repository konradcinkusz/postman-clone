namespace PostmanClone.Web.History;

/// <summary>
/// The addresses of the history pages (WP22). The list state lives in the query: a filter, a page and the selected entry, so a
/// page can be bookmarked and the back button returns to the same list. Defaults are left out of the address.
/// </summary>
public static class HistoryLinks
{
    public const string Path = "/history";

    public static string List(string? filter, int page, Guid? entryId = null)
    {
        var parts = new List<string>();
        if (!string.IsNullOrEmpty(filter))
        {
            parts.Add("q=" + Uri.EscapeDataString(filter));
        }

        if (page > 1)
        {
            parts.Add("page=" + page.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        if (entryId is { } id)
        {
            parts.Add("entry=" + id.ToString("D"));
        }

        return parts.Count == 0 ? Path : Path + "?" + string.Join('&', parts);
    }

    /// <summary>The workspace home, asked to open a history entry as a new draft (WP22). The home reads the entry itself.</summary>
    public static string OpenInBuilder(Guid entryId) => "/?reopen=" + entryId.ToString("D");
}
