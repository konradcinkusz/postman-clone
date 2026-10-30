namespace PostmanClone.Web.Components.Layout;

public sealed record NavItem(string Href, string Label);

/// <summary>
/// The navigation, in the order CONTRACTS §5 pins. A test asserts this order, so an edit cannot reorder the
/// product's pages silently.
/// </summary>
public static class NavItems
{
    public static readonly IReadOnlyList<NavItem> All =
    [
        new("/", "Home"),
        new("/environments", "Environments"),
        new("/history", "History"),
        new("/import", "Import"),
        new("/workspace", "Workspace"),
        new("/about", "About"),
    ];
}
