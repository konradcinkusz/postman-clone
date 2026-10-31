using PostmanClone.Web.Api;

namespace PostmanClone.Web.Collections;

public enum TreeNodeKind
{
    Folder,
    Request,
}

/// <summary>
/// One folder or request of a collection, as the tree shows it. The collection itself is the root and is not a node.
/// <see cref="Children"/> lists the folders first, then the requests, each in sort order.
/// </summary>
public sealed record TreeNode(TreeNodeKind Kind, Guid Id, string Name, string? Method, IReadOnlyList<TreeNode> Children);

/// <summary>
/// Something in the data that the tree could not place where its record says. An in-collection item whose parent is
/// missing, or that sits in a parent loop, is still shown at the top of its collection, so it does not vanish. An item
/// that belongs to another collection is left out, because it cannot belong here. Both are reported, and the page says so
/// (BLAZOR-WEB-APP §10: never claim more than the data shows).
/// </summary>
public enum TreeIssueKind
{
    /// <summary>A folder or request names a parent that is not in this collection, or belongs to another collection.</summary>
    Orphan,

    /// <summary>A folder is part of a parent loop (or is its own parent), so no path from the top reaches it.</summary>
    Cycle,

    /// <summary>The same id appears twice. The first occurrence is shown.</summary>
    Duplicate,

    /// <summary>The item names this collection but belongs to another one, so it is left out.</summary>
    Foreign,
}

public sealed record TreeIssue(TreeIssueKind Kind, Guid Id, string Name);

/// <summary>A folder as a destination for a move, with its path from the top of the collection, such as "Auth / Tokens".</summary>
public sealed record FolderChoice(Guid Id, string Path);

/// <summary>One row of the tree view: a node, its depth (1 is directly under the collection) and whether its children show.</summary>
public sealed record TreeRow(TreeNode Node, int Depth, bool HasChildren, bool Expanded);

/// <summary>
/// A collection's folders and requests built into a tree from the flat <see cref="CollectionTree"/> (CONTRACTS §2: the
/// client builds the tree from <c>parentFolderId</c>). Pure logic: no renderer, no HTTP (BLAZOR-WEB-APP §10).
/// Bad data never breaks the build; <see cref="Issues"/> says what was placed or left out.
/// </summary>
public sealed class CollectionTreeModel
{
    private readonly IReadOnlyList<TreeNode> _roots;
    private readonly IReadOnlyDictionary<Guid, TreeNode> _nodes;

    private CollectionTreeModel(CollectionDto collection, IReadOnlyList<TreeNode> roots, IReadOnlyList<TreeIssue> issues, IReadOnlyDictionary<Guid, TreeNode> nodes)
    {
        Collection = collection;
        _roots = roots;
        Issues = issues;
        _nodes = nodes;
    }

    public CollectionDto Collection { get; }

    /// <summary>The top-level folders, then the top-level requests.</summary>
    public IReadOnlyList<TreeNode> Roots => _roots;

    public IReadOnlyList<TreeIssue> Issues { get; }

    /// <summary>Builds the tree from one collection's flat listing.</summary>
    public static CollectionTreeModel From(CollectionTree source) => new TreeBuilder(source).Build();

    /// <summary>The node with this id, or null when the collection holds no such folder or request.</summary>
    public TreeNode? Find(Guid id) => _nodes.GetValueOrDefault(id);

    /// <summary>
    /// Every request id at or under a node, or under the collection when <paramref name="nodeId"/> is null. A folder's
    /// delete removes all of these (CONTRACTS §2 cascades), so the page closes their open tabs.
    /// </summary>
    public IReadOnlyList<Guid> RequestIdsWithin(Guid? nodeId)
    {
        var result = new List<Guid>();
        if (nodeId is null)
        {
            Collect(_roots, result);
        }
        else if (Find(nodeId.Value) is { } node)
        {
            Collect([node], result);
        }

        return result;

        static void Collect(IReadOnlyList<TreeNode> nodes, List<Guid> into)
        {
            foreach (var node in nodes)
            {
                if (node.Kind == TreeNodeKind.Request)
                {
                    into.Add(node.Id);
                }
                else
                {
                    Collect(node.Children, into);
                }
            }
        }
    }

    /// <summary>Every folder as a move destination, in tree order, with its path. A request may move to any of them.</summary>
    public IReadOnlyList<FolderChoice> FolderChoices()
    {
        var result = new List<FolderChoice>();
        Walk(_roots, prefix: null, result);
        return result;

        static void Walk(IReadOnlyList<TreeNode> nodes, string? prefix, List<FolderChoice> into)
        {
            foreach (var folder in nodes.Where(node => node.Kind == TreeNodeKind.Folder))
            {
                var path = prefix is null ? folder.Name : $"{prefix} / {folder.Name}";
                into.Add(new FolderChoice(folder.Id, path));
                Walk(folder.Children, path, into);
            }
        }
    }

    /// <summary>
    /// The rows under the collection, depth first, for the tree view. Without a filter a folder's children show only
    /// when its id is in <paramref name="expanded"/>. With a filter, a node shows when its name matches or something
    /// under it does, its ancestors show with it, and every such folder is open.
    /// </summary>
    public IReadOnlyList<TreeRow> Rows(IReadOnlySet<Guid> expanded, string? filter)
    {
        var active = TreeFilter.Normalize(filter);
        var matching = active is null ? null : MatchingIds(active);
        var rows = new List<TreeRow>();
        AddRows(_roots, depth: 1, expanded, matching, rows);
        return rows;
    }

    private static void AddRows(IReadOnlyList<TreeNode> nodes, int depth, IReadOnlySet<Guid> expanded, IReadOnlySet<Guid>? matching, List<TreeRow> into)
    {
        foreach (var node in nodes)
        {
            if (matching is not null && !matching.Contains(node.Id))
            {
                continue;
            }

            var isOpen = matching is not null || expanded.Contains(node.Id);
            into.Add(new TreeRow(node, depth, HasChildren: node.Children.Count > 0, Expanded: isOpen));

            if (node.Kind == TreeNodeKind.Folder && isOpen)
            {
                AddRows(node.Children, depth + 1, expanded, matching, into);
            }
        }
    }

    /// <summary>The ids of nodes whose name matches, and of every ancestor of such a node.</summary>
    private HashSet<Guid> MatchingIds(string filter)
    {
        var matches = new HashSet<Guid>();
        foreach (var root in _roots)
        {
            MarkMatches(root, filter, matches);
        }

        return matches;

        static bool MarkMatches(TreeNode node, string filter, HashSet<Guid> matches)
        {
            var childMatches = false;
            foreach (var child in node.Children)
            {
                childMatches |= MarkMatches(child, filter, matches);
            }

            if (TreeFilter.Contains(node.Name, filter) || childMatches)
            {
                matches.Add(node.Id);
                return true;
            }

            return false;
        }
    }

    /// <summary>
    /// Builds the tree in one pass. A node is placed once, so a loop in the data cannot recurse forever, and every
    /// folder ends up placed exactly once: reachable from the top, or placed at the top as a cycle.
    /// </summary>
    private sealed class TreeBuilder(CollectionTree source)
    {
        private readonly Dictionary<Guid, FolderDto> _folders = [];
        private readonly Dictionary<Guid, List<FolderDto>> _subfolders = [];
        private readonly Dictionary<Guid, List<RequestSummary>> _requestsByFolder = [];
        private readonly List<RequestSummary> _topRequests = [];
        private readonly HashSet<Guid> _requestIds = [];
        private readonly HashSet<Guid> _placed = [];
        private readonly Dictionary<Guid, TreeNode> _nodes = [];
        private readonly List<TreeIssue> _issues = [];

        public CollectionTreeModel Build()
        {
            IndexFolders();
            IndexRequests();

            var topFolders = new List<FolderDto>();
            foreach (var folder in _folders.Values)
            {
                if (folder.ParentFolderId is { } parentId && _folders.ContainsKey(parentId))
                {
                    _subfolders.GetOrAdd(parentId).Add(folder);
                }
                else
                {
                    if (folder.ParentFolderId is not null)
                    {
                        _issues.Add(new TreeIssue(TreeIssueKind.Orphan, folder.Id, folder.Name));
                    }

                    topFolders.Add(folder);
                }
            }

            var folderRoots = new List<TreeNode>();
            foreach (var folder in Sorted(topFolders))
            {
                folderRoots.Add(BuildFolder(folder));
            }

            // Whatever no top-level folder reached sits in a loop of parents: place each at the top, once, and report it.
            foreach (var folder in Sorted(_folders.Values))
            {
                if (!_placed.Contains(folder.Id))
                {
                    _issues.Add(new TreeIssue(TreeIssueKind.Cycle, folder.Id, folder.Name));
                    folderRoots.Add(BuildFolder(folder));
                }
            }

            var roots = new List<TreeNode>(folderRoots);
            roots.AddRange(Sorted(_topRequests).Select(RequestNode));

            return new CollectionTreeModel(source.Collection, roots, _issues, _nodes);
        }

        /// <summary>Keeps the first folder of each id. A folder of another collection is left out and reported.</summary>
        private void IndexFolders()
        {
            foreach (var folder in source.Folders)
            {
                if (folder.CollectionId != source.Collection.Id)
                {
                    _issues.Add(new TreeIssue(TreeIssueKind.Foreign, folder.Id, folder.Name));
                }
                else if (!_folders.TryAdd(folder.Id, folder))
                {
                    _issues.Add(new TreeIssue(TreeIssueKind.Duplicate, folder.Id, folder.Name));
                }
            }
        }

        /// <summary>Groups the requests by the folder they sit in. A request whose folder is missing goes to the top.</summary>
        private void IndexRequests()
        {
            foreach (var request in source.Requests)
            {
                if (request.CollectionId != source.Collection.Id)
                {
                    _issues.Add(new TreeIssue(TreeIssueKind.Foreign, request.Id, request.Name));
                    continue;
                }

                if (!_requestIds.Add(request.Id))
                {
                    _issues.Add(new TreeIssue(TreeIssueKind.Duplicate, request.Id, request.Name));
                    continue;
                }

                if (request.FolderId is { } folderId && !_folders.ContainsKey(folderId))
                {
                    _issues.Add(new TreeIssue(TreeIssueKind.Orphan, request.Id, request.Name));
                    _topRequests.Add(request);
                }
                else if (request.FolderId is { } placedIn)
                {
                    _requestsByFolder.GetOrAdd(placedIn).Add(request);
                }
                else
                {
                    _topRequests.Add(request);
                }
            }
        }

        private TreeNode BuildFolder(FolderDto folder)
        {
            _placed.Add(folder.Id);
            var children = new List<TreeNode>();
            if (_subfolders.TryGetValue(folder.Id, out var subfolders))
            {
                foreach (var child in Sorted(subfolders))
                {
                    if (!_placed.Contains(child.Id))
                    {
                        children.Add(BuildFolder(child));
                    }
                }
            }

            if (_requestsByFolder.TryGetValue(folder.Id, out var requests))
            {
                children.AddRange(Sorted(requests).Select(RequestNode));
            }

            var node = new TreeNode(TreeNodeKind.Folder, folder.Id, folder.Name, Method: null, children);
            _nodes[folder.Id] = node;
            return node;
        }

        private TreeNode RequestNode(RequestSummary request)
        {
            var node = new TreeNode(TreeNodeKind.Request, request.Id, request.Name, request.Method, []);
            _nodes[request.Id] = node;
            return node;
        }

        private static IEnumerable<FolderDto> Sorted(IEnumerable<FolderDto> folders) =>
            folders.OrderBy(folder => folder.SortOrder).ThenBy(folder => folder.Name, StringComparer.OrdinalIgnoreCase).ThenBy(folder => folder.Id);

        private static IEnumerable<RequestSummary> Sorted(IEnumerable<RequestSummary> requests) =>
            requests.OrderBy(request => request.SortOrder).ThenBy(request => request.Name, StringComparer.OrdinalIgnoreCase).ThenBy(request => request.Id);
    }
}

/// <summary>The filter box's rule, shared by the collection rows and the folder and request rows.</summary>
public static class TreeFilter
{
    /// <summary>The filter text trimmed, or null when there is nothing to filter by.</summary>
    public static string? Normalize(string? filter) => string.IsNullOrWhiteSpace(filter) ? null : filter.Trim();

    /// <summary>A case-insensitive "contains" on the name. An empty filter matches everything.</summary>
    public static bool Contains(string name, string? filter) =>
        Normalize(filter) is not { } text || name.Contains(text, StringComparison.OrdinalIgnoreCase);
}

/// <summary>Adds a value to the list stored under a key, creating the list on first use.</summary>
internal static class ListMapExtensions
{
    public static List<TValue> GetOrAdd<TKey, TValue>(this Dictionary<TKey, List<TValue>> map, TKey key) where TKey : notnull
    {
        if (!map.TryGetValue(key, out var list))
        {
            list = [];
            map[key] = list;
        }

        return list;
    }
}
