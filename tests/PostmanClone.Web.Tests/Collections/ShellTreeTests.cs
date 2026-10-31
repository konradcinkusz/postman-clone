using PostmanClone.Web.Api;
using PostmanClone.Web.Collections;

namespace PostmanClone.Web.Tests.Collections;

/// <summary>The flat rows the tree view draws: order, the open state, the filter, and a collection whose tree is not loaded yet.</summary>
public sealed class ShellTreeTests
{
    private static CollectionSummary Summary(Guid id, string name) =>
        new(id, name, null, Guid.NewGuid(), DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, RequestCount: 0);

    private static CollectionTreeModel Model(Guid collection, string name, params (Guid Id, string Name)[] requests) => CollectionTreeModel.From(new CollectionTree(
        new CollectionDto(collection, name, null, Guid.NewGuid(), DateTimeOffset.UtcNow, DateTimeOffset.UtcNow),
        [],
        requests.Select(request => new RequestSummary(request.Id, collection, null, request.Name, "GET", 0, DateTimeOffset.UtcNow)).ToList()));

    [Fact]
    public void Collections_are_listed_by_name_and_a_collection_that_is_not_loaded_is_a_row_with_no_children()
    {
        var beta = Guid.NewGuid();
        var alpha = Guid.NewGuid();
        var rows = ShellTree.Build([Summary(beta, "beta"), Summary(alpha, "Alpha")], new Dictionary<Guid, CollectionTreeModel>(), new HashSet<Guid>(), filter: null);

        Assert.Equal(["Alpha", "beta"], rows.Select(row => row.Name));
        Assert.All(rows, row => Assert.Equal(ShellRowKind.Collection, row.Kind));
        Assert.All(rows, row => Assert.False(row.Expanded));
    }

    [Fact]
    public void An_open_loaded_collection_shows_its_requests_and_each_row_knows_its_collection()
    {
        var collection = Guid.NewGuid();
        var refund = Guid.NewGuid();
        var trees = new Dictionary<Guid, CollectionTreeModel> { [collection] = Model(collection, "Billing", (refund, "Refund")) };

        var rows = ShellTree.Build([Summary(collection, "Billing")], trees, new HashSet<Guid> { collection }, filter: null);

        Assert.Equal(2, rows.Count);
        Assert.Equal(ShellRowKind.Request, rows[1].Kind);
        Assert.Equal(refund, rows[1].Id);
        Assert.Equal(collection, rows[1].CollectionId);
        Assert.Equal(1, rows[1].Depth);
    }

    [Fact]
    public void A_filter_hides_collections_with_nothing_that_matches_and_opens_the_ones_that_show()
    {
        var billing = Guid.NewGuid();
        var auth = Guid.NewGuid();
        var trees = new Dictionary<Guid, CollectionTreeModel>
        {
            [billing] = Model(billing, "Billing", (Guid.NewGuid(), "Refund")),
            [auth] = Model(auth, "Auth", (Guid.NewGuid(), "Login")),
        };

        var rows = ShellTree.Build([Summary(billing, "Billing"), Summary(auth, "Auth")], trees, new HashSet<Guid>(), filter: "refund");

        Assert.Equal(["Billing", "Refund"], rows.Select(row => row.Name));
        Assert.True(rows[0].Expanded);
    }

    [Fact]
    public void A_collection_whose_name_matches_shows_even_with_no_matching_content()
    {
        var billing = Guid.NewGuid();
        var rows = ShellTree.Build([Summary(billing, "Billing")], new Dictionary<Guid, CollectionTreeModel>(), new HashSet<Guid>(), filter: "bill");

        Assert.Equal(["Billing"], rows.Select(row => row.Name));
    }
}
