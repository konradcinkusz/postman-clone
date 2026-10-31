using PostmanClone.Web.Api;

namespace PostmanClone.Web.History;

/// <summary>Where a page of the history list is: its number, how many pages there are, and which entries it shows.</summary>
public sealed record HistoryPage(int Page, int PageCount, bool HasPrevious, bool HasNext, int First, int Last, int Total);

/// <summary>
/// The paging model of the history list (WP22). It is pure arithmetic: a requested page is clamped into the pages that exist, an
/// empty history is one empty page, and the range text says which entries are shown. No renderer, so the rules are tested alone.
/// </summary>
public static class HistoryPaging
{
    /// <summary>How many entries the history page shows at once.</summary>
    public const int PageSize = 20;

    /// <summary>How many of the newest entries the workspace home panel shows.</summary>
    public const int PanelSize = 10;

    /// <summary>The page the list should show for a requested number and a total. A page past the end is the last page.</summary>
    public static HistoryPage Plan(int? requestedPage, int total, int pageSize = PageSize)
    {
        var size = Math.Clamp(pageSize, 1, HistoryClient.MaximumPageSize);
        var safeTotal = Math.Max(0, total);
        var count = Math.Max(1, (int)((safeTotal + size - 1L) / size));
        var page = Math.Clamp(requestedPage ?? 1, 1, count);
        var first = safeTotal == 0 ? 0 : (page - 1) * size + 1;
        var last = safeTotal == 0 ? 0 : Math.Min(safeTotal, page * size);
        return new HistoryPage(page, count, page > 1, page < count, first, last, safeTotal);
    }

    /// <summary>The line above the list: which entries are shown, or that there are none.</summary>
    public static string Range(HistoryPage page) =>
        page.Total == 0 ? "No entries" : $"Showing {page.First}–{page.Last} of {page.Total}";

    /// <summary>
    /// The filter as the list sends it: trimmed, and cut to <see cref="MaximumFilterLength"/> characters, so a long pasted filter
    /// stays a short query string. Empty means no filter.
    /// </summary>
    public static string? NormalizeFilter(string? filter)
    {
        var trimmed = filter?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return null;
        }

        return trimmed.Length <= MaximumFilterLength ? trimmed : trimmed[..MaximumFilterLength];
    }

    /// <summary>The longest filter the page sends. A search for a whole URL rarely needs more.</summary>
    public const int MaximumFilterLength = 200;
}
