using PostmanClone.Web.History;

namespace PostmanClone.Web.Tests.History;

/// <summary>The paging model and the filter and link rules of the history list (WP22), as plain arithmetic.</summary>
public sealed class HistoryPagingTests
{
    [Fact]
    public void An_empty_history_is_one_empty_page()
    {
        var page = HistoryPaging.Plan(null, 0);

        Assert.Equal(1, page.Page);
        Assert.Equal(1, page.PageCount);
        Assert.False(page.HasPrevious);
        Assert.False(page.HasNext);
        Assert.Equal("No entries", HistoryPaging.Range(page));
    }

    [Fact]
    public void The_first_page_of_several_has_a_next_and_no_previous()
    {
        var page = HistoryPaging.Plan(1, 45, 20);

        Assert.Equal(3, page.PageCount);
        Assert.False(page.HasPrevious);
        Assert.True(page.HasNext);
        Assert.Equal("Showing 1–20 of 45", HistoryPaging.Range(page));
    }

    [Fact]
    public void The_last_page_shows_the_remainder_and_has_no_next()
    {
        var page = HistoryPaging.Plan(3, 45, 20);

        Assert.Equal("Showing 41–45 of 45", HistoryPaging.Range(page));
        Assert.True(page.HasPrevious);
        Assert.False(page.HasNext);
    }

    [Fact]
    public void A_page_past_the_end_is_the_last_page()
    {
        var page = HistoryPaging.Plan(9, 45, 20);

        Assert.Equal(3, page.Page);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-4)]
    public void A_page_before_the_first_is_the_first_page(int requested)
    {
        Assert.Equal(1, HistoryPaging.Plan(requested, 45, 20).Page);
    }

    [Theory]
    [InlineData(20, 1)]
    [InlineData(21, 2)]
    public void A_total_exactly_one_page_long_is_one_page_and_one_more_is_two(int total, int pages)
    {
        Assert.Equal(pages, HistoryPaging.Plan(1, total, 20).PageCount);
    }

    [Fact]
    public void The_page_size_is_kept_within_the_service_maximum()
    {
        var page = HistoryPaging.Plan(1, 150, pageSize: 500);

        Assert.Equal(2, page.PageCount);
        Assert.Equal(100, page.Last);
    }

    [Fact]
    public void A_filter_is_trimmed_and_an_empty_one_is_no_filter()
    {
        Assert.Null(HistoryPaging.NormalizeFilter(null));
        Assert.Null(HistoryPaging.NormalizeFilter("   "));
        Assert.Equal("api/items", HistoryPaging.NormalizeFilter("  api/items  "));
    }

    [Fact]
    public void A_long_filter_is_cut_to_the_filter_limit()
    {
        var filter = NewText(HistoryPaging.MaximumFilterLength + 50);

        var normalized = HistoryPaging.NormalizeFilter(filter);

        Assert.Equal(HistoryPaging.MaximumFilterLength, normalized!.Length);
        Assert.Equal(filter[..HistoryPaging.MaximumFilterLength], normalized);
    }

    [Fact]
    public void The_list_address_leaves_out_defaults_and_keeps_the_state()
    {
        Assert.Equal("/history", HistoryLinks.List(null, 1));
        Assert.Equal("/history?q=items%3Fx&page=3", HistoryLinks.List("items?x", 3));

        var entry = Guid.Parse("11111111-2222-3333-4444-555555555555");
        Assert.Equal("/history?entry=11111111-2222-3333-4444-555555555555", HistoryLinks.List(null, 1, entry));
    }

    [Fact]
    public void Open_in_builder_asks_the_home_page_for_the_entry()
    {
        var entry = Guid.Parse("11111111-2222-3333-4444-555555555555");

        Assert.Equal("/?reopen=11111111-2222-3333-4444-555555555555", HistoryLinks.OpenInBuilder(entry));
    }

    private static string NewText(int length) => new('a', length);
}
