using PostmanClone.CollectionsService.Domain;
using Xunit;

namespace PostmanClone.CollectionsService.Tests.Folders;

/// <summary>
/// The tree walks behind the depth and cycle rules, without a database or a host. The deep-chain tests use a chain far
/// longer than the limit of eight levels, which a recursive walk would have to descend.
/// </summary>
public sealed class FolderTreeTests
{
    private static readonly Guid Collection = Guid.NewGuid();

    [Fact]
    public void Depth_counts_levels_from_the_top_and_is_zero_for_the_collection_itself()
    {
        var root = Folder("root", null);
        var child = Folder("child", root);
        var tree = new FolderTree([root, child]);

        Assert.Equal(0, tree.DepthOf(null));
        Assert.Equal(1, tree.DepthOf(root.Id));
        Assert.Equal(2, tree.DepthOf(child.Id));
    }

    [Fact]
    public void Height_counts_the_folder_and_its_deepest_descendant()
    {
        var top = Folder("top", null);
        var middle = Folder("middle", top);
        var leaf = Folder("leaf", middle);
        var sibling = Folder("sibling", top);
        var tree = new FolderTree([top, middle, leaf, sibling]);

        Assert.Equal(3, tree.HeightOf(top.Id));
        Assert.Equal(1, tree.HeightOf(leaf.Id));
        Assert.Equal(1, tree.HeightOf(sibling.Id));
    }

    [Fact]
    public void Children_come_in_sort_order_then_by_id()
    {
        var late = Folder("late", null, sortOrder: 2);
        var early = Folder("early", null, sortOrder: 0);
        var middle = Folder("middle", null, sortOrder: 1);
        var tree = new FolderTree([late, early, middle]);

        Assert.Equal(new[] { "early", "middle", "late" }, tree.ChildrenOf(null).Select(folder => folder.Name));
    }

    [Fact]
    public void A_folder_moved_into_itself_or_into_a_descendant_would_cycle()
    {
        var top = Folder("top", null);
        var middle = Folder("middle", top);
        var leaf = Folder("leaf", middle);
        var sibling = Folder("sibling", null);
        var tree = new FolderTree([top, middle, leaf, sibling]);

        Assert.True(tree.WouldCycle(top.Id, top.Id));
        Assert.True(tree.WouldCycle(top.Id, leaf.Id));
        Assert.False(tree.WouldCycle(middle.Id, sibling.Id));
        Assert.False(tree.WouldCycle(top.Id, null));
    }

    [Fact]
    public void A_chain_of_ten_thousand_levels_is_walked_without_recursion()
    {
        var chain = new List<Folder>(10_000);
        Guid? parent = null;
        for (var level = 0; level < 10_000; level++)
        {
            var folder = new Folder { Id = Guid.NewGuid(), WorkspaceId = Guid.NewGuid(), CollectionId = Collection, ParentFolderId = parent, Name = $"level {level}" };
            chain.Add(folder);
            parent = folder.Id;
        }

        var tree = new FolderTree(chain);

        Assert.Equal(10_000, tree.DepthOf(chain[^1].Id));
        Assert.Equal(10_000, tree.HeightOf(chain[0].Id));
        Assert.True(tree.WouldCycle(chain[0].Id, chain[^1].Id));
        Assert.False(tree.WouldCycle(chain[^1].Id, chain[0].Id));
    }

    [Fact]
    public void A_loop_in_the_stored_data_is_reported_instead_of_walked_forever()
    {
        var first = Folder("first", null);
        var second = Folder("second", first);
        first.ParentFolderId = second.Id;
        var tree = new FolderTree([first, second]);

        Assert.Throws<InvalidOperationException>(() => tree.DepthOf(first.Id));
        Assert.True(tree.WouldCycle(first.Id, second.Id));
    }

    private static Folder Folder(string name, Folder? parent, int sortOrder = 0) => new()
    {
        Id = Guid.NewGuid(),
        WorkspaceId = Guid.NewGuid(),
        CollectionId = Collection,
        ParentFolderId = parent?.Id,
        Name = name,
        SortOrder = sortOrder,
    };
}
