using PostmanClone.Web.Api;

namespace PostmanClone.Web.Collections;

public enum ShellRowKind
{
    Collection,
    Folder,
    Request,
}

/// <summary>
/// One line of the workspace tree: a collection (depth 0), or a folder or request inside one (depth 1 and deeper).
/// <see cref="CollectionId"/> is the collection the line belongs to, which the actions need.
/// </summary>
public sealed record ShellRow(
    ShellRowKind Kind,
    Guid Id,
    Guid CollectionId,
    string Name,
    string? Method,
    int Depth,
    bool HasChildren,
    bool Expanded);

/// <summary>
/// The workspace tree as one flat list, which is what the view renders: each collection, then, when it is open and loaded,
/// its folders and requests in tree order. Pure function over the page's state, so the expand, filter and failure rules
/// are tested with xUnit (BLAZOR-WEB-APP §10).
/// </summary>
public static class ShellTree
{
    /// <summary>
    /// Builds the rows. A collection's children show only when it is open (in <paramref name="expanded"/>) and loaded. A
    /// filter applies to the loaded contents: a collection shows when its name matches or something in it does, and every
    /// shown collection is open. A collection that is not loaded yet shows as a row with no children.
    /// </summary>
    public static IReadOnlyList<ShellRow> Build(
        IReadOnlyList<CollectionSummary> collections,
        IReadOnlyDictionary<Guid, CollectionTreeModel> trees,
        IReadOnlySet<Guid> expanded,
        string? filter)
    {
        var text = TreeFilter.Normalize(filter);
        var rows = new List<ShellRow>();
        foreach (var collection in collections.OrderBy(collection => collection.Name, StringComparer.OrdinalIgnoreCase).ThenBy(collection => collection.Id))
        {
            var loaded = trees.GetValueOrDefault(collection.Id);
            IReadOnlyList<TreeRow> children = loaded is null ? Array.Empty<TreeRow>() : loaded.Rows(expanded, text);
            if (text is not null && !TreeFilter.Contains(collection.Name, text) && children.Count == 0)
            {
                continue;
            }

            var open = text is not null || expanded.Contains(collection.Id);
            rows.Add(new ShellRow(ShellRowKind.Collection, collection.Id, collection.Id, collection.Name, Method: null, Depth: 0, HasChildren: true, Expanded: open));

            if (open)
            {
                foreach (var child in children)
                {
                    rows.Add(new ShellRow(
                        child.Node.Kind == TreeNodeKind.Folder ? ShellRowKind.Folder : ShellRowKind.Request,
                        child.Node.Id,
                        collection.Id,
                        child.Node.Name,
                        child.Node.Method,
                        child.Depth,
                        child.HasChildren,
                        child.Expanded));
                }
            }
        }

        return rows;
    }
}
