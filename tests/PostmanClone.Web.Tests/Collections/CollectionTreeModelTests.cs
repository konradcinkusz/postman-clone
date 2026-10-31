using PostmanClone.Web.Api;
using PostmanClone.Web.Collections;

namespace PostmanClone.Web.Tests.Collections;

/// <summary>
/// The tree built from the flat listing (CONTRACTS §2). Plain xUnit: no renderer, no HTTP. Bad data (orphans, loops, duplicates,
/// foreign items) must never break the build, and every item the data holds is either placed or reported.
/// </summary>
public sealed class CollectionTreeModelTests
{
    private static readonly Guid Collection = Guid.NewGuid();

    private static CollectionTree Listing(List<FolderDto>? folders = null, List<RequestSummary>? requests = null) => new(
        new CollectionDto(Collection, "Billing", null, Guid.NewGuid(), DateTimeOffset.UtcNow, DateTimeOffset.UtcNow),
        folders ?? [],
        requests ?? []);

    private static FolderDto Folder(Guid id, string name, Guid? parent = null, int sortOrder = 0, Guid? collection = null) =>
        new(id, collection ?? Collection, parent, name, sortOrder);

    private static RequestSummary Request(string name, Guid? folder = null, int sortOrder = 0, Guid? id = null, Guid? collection = null) =>
        new(id ?? Guid.NewGuid(), collection ?? Collection, folder, name, "GET", sortOrder, DateTimeOffset.UtcNow);

    private static IEnumerable<TreeNode> Walk(IEnumerable<TreeNode> nodes)
    {
        foreach (var node in nodes)
        {
            yield return node;
            foreach (var child in Walk(node.Children))
            {
                yield return child;
            }
        }
    }

    [Fact]
    public void Folders_come_first_then_requests_and_each_group_is_in_sort_order()
    {
        var first = Guid.NewGuid();
        var model = CollectionTreeModel.From(Listing(
            folders: [Folder(Guid.NewGuid(), "Zeta", sortOrder: 1), Folder(first, "Alpha", sortOrder: 0)],
            requests: [Request("Aardvark", sortOrder: 0), Request("Bee", sortOrder: 2)]));

        Assert.Equal(["Alpha", "Zeta", "Aardvark", "Bee"], model.Roots.Select(node => node.Name));
        Assert.Equal(TreeNodeKind.Folder, model.Roots[0].Kind);
        Assert.Equal(TreeNodeKind.Request, model.Roots[3].Kind);
        Assert.Empty(model.Issues);
    }

    [Fact]
    public void Items_nest_under_their_parents_at_any_depth()
    {
        var top = Guid.NewGuid();
        var middle = Guid.NewGuid();
        var deep = Guid.NewGuid();
        var model = CollectionTreeModel.From(Listing(
            folders: [Folder(top, "Top"), Folder(middle, "Middle", parent: top), Folder(deep, "Deep", parent: middle)],
            requests: [Request("Leaf", folder: deep)]));

        var leaf = model.Find(model.Roots[0].Children[0].Children[0].Children[0].Id);
        Assert.Equal("Leaf", leaf!.Name);
        Assert.Equal("Deep", model.Roots[0].Children[0].Children[0].Name);
        Assert.Empty(model.Issues);
    }

    [Fact]
    public void A_request_whose_folder_is_missing_is_shown_at_the_top_and_reported_as_an_orphan()
    {
        var lost = Request("Lost", folder: Guid.NewGuid());
        var model = CollectionTreeModel.From(Listing(requests: [lost]));

        Assert.Equal("Lost", Assert.Single(model.Roots).Name);
        var issue = Assert.Single(model.Issues);
        Assert.Equal(TreeIssueKind.Orphan, issue.Kind);
        Assert.Equal(lost.Id, issue.Id);
    }

    [Fact]
    public void A_folder_whose_parent_is_missing_is_shown_at_the_top_and_reported_as_an_orphan()
    {
        var child = Folder(Guid.NewGuid(), "Child", parent: Guid.NewGuid());
        var model = CollectionTreeModel.From(Listing(folders: [child]));

        Assert.Equal("Child", Assert.Single(model.Roots).Name);
        Assert.Equal(TreeIssueKind.Orphan, Assert.Single(model.Issues).Kind);
    }

    [Fact]
    public void Two_folders_that_name_each_other_as_parent_are_both_shown_once_and_one_is_reported_as_a_cycle()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var model = CollectionTreeModel.From(Listing(folders: [Folder(a, "A", parent: b), Folder(b, "B", parent: a)]));

        var everything = Walk(model.Roots).ToList();
        Assert.Equal(2, everything.Count);
        Assert.Equal([a, b], everything.Select(node => node.Id));
        Assert.Equal(TreeIssueKind.Cycle, Assert.Single(model.Issues).Kind);
    }

    [Fact]
    public void A_folder_that_is_its_own_parent_is_a_cycle_and_is_shown_once()
    {
        var self = Guid.NewGuid();
        var model = CollectionTreeModel.From(Listing(folders: [Folder(self, "Loop", parent: self)]));

        Assert.Equal("Loop", Assert.Single(model.Roots).Name);
        Assert.Equal(TreeIssueKind.Cycle, Assert.Single(model.Issues).Kind);
    }

    [Fact]
    public void A_duplicated_folder_id_keeps_the_first_and_reports_the_second()
    {
        var id = Guid.NewGuid();
        var model = CollectionTreeModel.From(Listing(folders: [Folder(id, "First"), Folder(id, "Second")]));

        Assert.Equal("First", Assert.Single(model.Roots).Name);
        Assert.Equal(TreeIssueKind.Duplicate, Assert.Single(model.Issues).Kind);
    }

    [Fact]
    public void Items_of_another_collection_are_left_out_and_reported_as_foreign()
    {
        var elsewhere = Guid.NewGuid();
        var model = CollectionTreeModel.From(Listing(
            folders: [Folder(Guid.NewGuid(), "Not mine", collection: elsewhere)],
            requests: [Request("Not mine either", collection: elsewhere)]));

        Assert.Empty(model.Roots);
        Assert.Equal(2, model.Issues.Count(issue => issue.Kind == TreeIssueKind.Foreign));
    }

    [Fact]
    public void Request_ids_within_a_folder_include_the_ones_nested_under_it_and_nothing_else()
    {
        var folder = Guid.NewGuid();
        var nested = Guid.NewGuid();
        var direct = Request("Direct", folder: folder);
        var deep = Request("Deep", folder: nested);
        var outside = Request("Outside");
        var model = CollectionTreeModel.From(Listing(
            folders: [Folder(folder, "Outer"), Folder(nested, "Inner", parent: folder)],
            requests: [direct, deep, outside]));

        var within = model.RequestIdsWithin(folder);

        Assert.Equal(2, within.Count);
        Assert.Contains(direct.Id, within);
        Assert.Contains(deep.Id, within);
        Assert.DoesNotContain(outside.Id, within);
        Assert.Equal(3, model.RequestIdsWithin(null).Count);
        Assert.Equal([deep.Id], model.RequestIdsWithin(deep.Id));
        Assert.Empty(model.RequestIdsWithin(Guid.NewGuid()));
    }

    [Fact]
    public void Folder_choices_show_the_path_from_the_top()
    {
        var auth = Guid.NewGuid();
        var tokens = Guid.NewGuid();
        var model = CollectionTreeModel.From(Listing(folders: [Folder(auth, "Auth"), Folder(tokens, "Tokens", parent: auth)]));

        var choices = model.FolderChoices();

        Assert.Equal(["Auth", "Auth / Tokens"], choices.Select(choice => choice.Path));
    }

    [Fact]
    public void Collapsed_folders_hide_their_children_and_expanded_ones_show_them()
    {
        var folder = Guid.NewGuid();
        var model = CollectionTreeModel.From(Listing(folders: [Folder(folder, "Folder")], requests: [Request("Inside", folder: folder)]));

        var collapsed = model.Rows(new HashSet<Guid>(), filter: null);
        var expanded = model.Rows(new HashSet<Guid> { folder }, filter: null);

        Assert.Equal(["Folder"], collapsed.Select(row => row.Node.Name));
        Assert.Equal(["Folder", "Inside"], expanded.Select(row => row.Node.Name));
        Assert.Equal([1, 2], expanded.Select(row => row.Depth));
        Assert.True(collapsed[0].HasChildren);
        Assert.False(collapsed[0].Expanded);
    }

    [Fact]
    public void A_filter_keeps_the_matches_with_their_ancestors_and_opens_them()
    {
        var folder = Guid.NewGuid();
        var model = CollectionTreeModel.From(Listing(
            folders: [Folder(folder, "Invoices"), Folder(Guid.NewGuid(), "Unrelated")],
            requests: [Request("Refund flow", folder: folder), Request("Other", folder: folder)]));

        var rows = model.Rows(new HashSet<Guid>(), filter: "  REFUND ");

        Assert.Equal(["Invoices", "Refund flow"], rows.Select(row => row.Node.Name));
        Assert.True(rows[0].Expanded);
    }

    [Fact]
    public void A_filter_that_matches_nothing_gives_no_rows_and_an_empty_filter_gives_them_all()
    {
        var model = CollectionTreeModel.From(Listing(requests: [Request("Refund")]));

        Assert.Empty(model.Rows(new HashSet<Guid>(), filter: "nothing like it"));
        Assert.Single(model.Rows(new HashSet<Guid>(), filter: "   "));
    }

    [Fact]
    public void A_chain_of_the_service_maximum_depth_builds_and_flattens()
    {
        var ids = Enumerable.Range(0, 8).Select(_ => Guid.NewGuid()).ToList();
        var folders = ids.Select((id, index) => Folder(id, $"Level {index}", parent: index == 0 ? null : ids[index - 1])).ToList();
        var model = CollectionTreeModel.From(Listing(folders: folders));

        var rows = model.Rows(new HashSet<Guid>(ids), filter: null);

        Assert.Equal(8, rows.Count);
        Assert.Equal(8, rows[^1].Depth);
        Assert.Empty(model.Issues);
    }
}
