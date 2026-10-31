namespace PostmanClone.CollectionsService.Domain;

/// <summary>
/// The folders of one collection as a tree, with the walks the rules need: depth, height and the cycle check
/// (CONTRACTS §2). Every walk is iterative, so a chain as deep as the data holds cannot exhaust the stack, and
/// every walk stops when the data loops back on itself.
/// </summary>
public sealed class FolderTree
{
    private static readonly IComparer<Folder> BySortOrder = Comparer<Folder>.Create(
        (left, right) => left.SortOrder != right.SortOrder
            ? left.SortOrder.CompareTo(right.SortOrder)
            : left.Id.CompareTo(right.Id));

    private readonly Dictionary<Guid, Folder> _byId;
    private readonly List<Folder> _topLevel = [];
    private readonly Dictionary<Guid, List<Folder>> _children = [];

    public FolderTree(IEnumerable<Folder> folders)
    {
        _byId = folders.ToDictionary(folder => folder.Id);
        foreach (var folder in _byId.Values)
        {
            if (folder.ParentFolderId is not { } parentId)
            {
                _topLevel.Add(folder);
                continue;
            }

            if (!_children.TryGetValue(parentId, out var siblings))
            {
                _children[parentId] = siblings = [];
            }

            siblings.Add(folder);
        }

        _topLevel.Sort(BySortOrder);
        foreach (var siblings in _children.Values)
        {
            siblings.Sort(BySortOrder);
        }
    }

    /// <summary>The folder with that id, or null when the collection holds none.</summary>
    public Folder? Find(Guid id) => _byId.GetValueOrDefault(id);

    /// <summary>The children of a folder in sort order (null: the top level). Empty when there are none.</summary>
    public IReadOnlyList<Folder> ChildrenOf(Guid? parentId)
    {
        if (parentId is null)
        {
            return _topLevel;
        }

        return _children.TryGetValue(parentId.Value, out var siblings) ? siblings : [];
    }

    /// <summary>The level of a folder, the top level being 1. Zero for null (the collection itself).</summary>
    public int DepthOf(Guid? folderId)
    {
        var depth = 0;
        var current = folderId;
        while (current is { } id && _byId.TryGetValue(id, out var folder))
        {
            depth++;
            if (depth > _byId.Count)
            {
                throw new InvalidOperationException("The folder tree loops back on itself.");
            }

            current = folder.ParentFolderId;
        }

        return depth;
    }

    /// <summary>The number of levels from the folder down to its deepest descendant, the folder itself counting as 1.</summary>
    public int HeightOf(Guid folderId)
    {
        var height = 0;
        var visited = new HashSet<Guid>();
        var level = new List<Guid> { folderId };
        while (level.Count > 0)
        {
            height++;
            var next = new List<Guid>();
            foreach (var id in level)
            {
                if (visited.Add(id))
                {
                    next.AddRange(ChildrenOf(id).Select(child => child.Id));
                }
            }

            level = next;
        }

        return height;
    }

    /// <summary>
    /// True when putting <paramref name="folderId"/> under <paramref name="newParentId"/> would make it its own ancestor:
    /// the new parent is the folder itself or one of its descendants.
    /// </summary>
    public bool WouldCycle(Guid folderId, Guid? newParentId)
    {
        var steps = 0;
        var current = newParentId;
        while (current is { } id)
        {
            if (id == folderId || ++steps > _byId.Count)
            {
                return true;
            }

            if (!_byId.TryGetValue(id, out var folder))
            {
                return false;
            }

            current = folder.ParentFolderId;
        }

        return false;
    }
}
