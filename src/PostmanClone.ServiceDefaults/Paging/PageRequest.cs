namespace PostmanClone.ServiceDefaults.Paging;

/// <summary>
/// A list page with its inputs clamped (CONTRACTS §0, SERVICE-API-PATTERNS §4): page at least 1,
/// page size 1..100, default 50. Clamping is a denial-of-service control, so every list endpoint uses it.
/// </summary>
public readonly record struct PageRequest(int Page, int PageSize)
{
    public const int DefaultPageSize = 50;
    public const int MaxPageSize = 100;

    /// <summary>Rows to skip. A long, not an int: <c>page</c> may be near int.MaxValue.</summary>
    public long Skip => (long)(Page - 1) * PageSize;

    public static PageRequest Clamp(int? page, int? pageSize) =>
        new(Math.Max(1, page ?? 1), Math.Clamp(pageSize ?? DefaultPageSize, 1, MaxPageSize));
}
