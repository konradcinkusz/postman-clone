using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using PostmanClone.ServiceDefaults.Authentication;
using PostmanClone.Web.History;
using PostmanClone.Web.Tests.Auth;
using PostmanClone.Web.Tests.History;
using PostmanClone.Web.Tests.Membership;
using HistoryPage = PostmanClone.Web.Components.Pages.History;

namespace PostmanClone.Web.Tests.HistoryPages;

/// <summary>
/// The request history page (WP22), rendered by bUnit: the caller's entries as the service returns them, the detail through the same
/// response panel as a live send (text only), paging and the filter in the address, the two-step deletes, and the missing and
/// unavailable answers. The fake history answers as the service does (ADR-0015).
/// </summary>
public sealed class HistoryPageTests
{
    private static CancellationToken Ct => Xunit.TestContext.Current.CancellationToken;

    private static async Task<(BunitContext Context, FakeHistoryClient History, FakeTimeProvider Clock)> SetUpAsync(AuthHarness harness)
    {
        var principal = await MembershipPageTestHarness.SignedInAsync(harness, Guid.NewGuid(), WorkspaceRoles.Member, Ct);
        var clock = new FakeTimeProvider();
        var history = new FakeHistoryClient();
        return (HistoryPageHarness.Create(harness, principal, history, clock), history, clock);
    }

    [Fact]
    public async Task It_lists_the_callers_entries_newest_first_with_the_masked_urls_as_returned()
    {
        using var harness = new AuthHarness();
        var (context, history, clock) = await SetUpAsync(harness);
        using var _ = context;
        var older = history.Add(HistoryFixtures.Entry(clock, 30, url: "https://api.example.test/older"));
        var newer = history.Add(HistoryFixtures.Entry(clock, 5, url: "https://api.example.test/items?api_key=" + HistoryFixtures.Mask));

        var cut = HistoryPageHarness.RenderAt<HistoryPage>(context, HistoryLinks.List(null, 1));

        cut.WaitForState(() => cut.FindAll("[data-testid='history-list']").Count == 1);
        var rows = cut.FindAll("[data-testid^='history-row-']");
        Assert.Equal(2, rows.Count);
        Assert.Equal($"history-row-{newer.Id}", rows[0].GetAttribute("data-testid"));
        Assert.Equal($"history-row-{older.Id}", rows[1].GetAttribute("data-testid"));
        Assert.Contains("api_key=••••••", cut.Find("[data-testid='history-list']").TextContent);
        Assert.Equal("list page=1 size=20 q=", history.Calls[0]);
        Assert.Contains("are not stored", cut.Find("[data-testid='history-mask-note']").TextContent);
    }

    [Fact]
    public async Task An_empty_history_says_nothing_has_been_sent_yet()
    {
        using var harness = new AuthHarness();
        var (context, _, _) = await SetUpAsync(harness);
        using var _ = context;

        var cut = HistoryPageHarness.RenderAt<HistoryPage>(context, HistoryLinks.List(null, 1));

        cut.WaitForState(() => cut.FindAll("[data-testid='history-empty']").Count == 1);
        Assert.Equal(HistoryMessages.Empty, cut.Find("[data-testid='history-empty']").TextContent.Trim());
    }

    [Fact]
    public async Task The_detail_shows_the_stored_result_as_text_and_creates_no_element_from_it()
    {
        using var harness = new AuthHarness();
        var (context, history, clock) = await SetUpAsync(harness);
        using var _ = context;
        var entry = history.Add(HistoryFixtures.Entry(clock, 5, body: "<script>alert('x')</script><b>bold</b>"));

        var cut = HistoryPageHarness.RenderAt<HistoryPage>(context, HistoryLinks.List(null, 1, entry.Id));

        cut.WaitForState(() => cut.FindAll("[data-testid='history-detail']").Count == 1);
        var detail = cut.Find("[data-testid='history-detail']");
        Assert.Empty(detail.QuerySelectorAll("script"));
        Assert.Empty(detail.QuerySelectorAll("b"));
        Assert.Contains("<script>alert('x')</script><b>bold</b>", detail.TextContent);
        Assert.Contains("&lt;script&gt;", cut.Markup);
    }

    [Fact]
    public async Task The_detail_shows_the_status_and_links_the_entry_to_the_builder()
    {
        using var harness = new AuthHarness();
        var (context, history, clock) = await SetUpAsync(harness);
        using var _ = context;
        var entry = history.Add(HistoryFixtures.Entry(clock, 5));

        var cut = HistoryPageHarness.RenderAt<HistoryPage>(context, HistoryLinks.List(null, 1, entry.Id));

        cut.WaitForState(() => cut.FindAll("[data-testid='response-status']").Count == 1);
        Assert.Equal("200 OK", cut.Find("[data-testid='response-status']").TextContent.Trim());
        Assert.Equal(HistoryLinks.OpenInBuilder(entry.Id), cut.Find("[data-testid='history-open-builder']").GetAttribute("href"));
    }

    [Fact]
    public async Task A_long_history_is_paged_and_the_last_page_has_no_older_link()
    {
        using var harness = new AuthHarness();
        var (context, history, clock) = await SetUpAsync(harness);
        using var _ = context;
        for (var minutes = 1; minutes <= 25; minutes++)
        {
            history.Add(HistoryFixtures.Entry(clock, minutes));
        }

        var cut = HistoryPageHarness.RenderAt<HistoryPage>(context, HistoryLinks.List(null, 2));

        cut.WaitForState(() => cut.FindAll("[data-testid='history-range']").Count == 1);
        Assert.Equal("Showing 21–25 of 25", cut.Find("[data-testid='history-range']").TextContent);
        Assert.Equal(5, cut.FindAll("[data-testid^='history-row-']").Count);
        Assert.Empty(cut.FindAll("[data-testid='history-next']"));
        Assert.Equal("/history", cut.Find("[data-testid='history-prev']").GetAttribute("href"));
    }

    [Fact]
    public async Task A_page_past_the_end_is_corrected_to_the_last_page()
    {
        using var harness = new AuthHarness();
        var (context, history, clock) = await SetUpAsync(harness);
        using var _ = context;
        for (var minutes = 1; minutes <= 25; minutes++)
        {
            history.Add(HistoryFixtures.Entry(clock, minutes));
        }

        HistoryPageHarness.RenderAt<HistoryPage>(context, HistoryLinks.List(null, 9));

        Assert.EndsWith("/history?page=2", context.Services.GetRequiredService<NavigationManager>().Uri, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_filter_is_sent_in_the_address_and_matches_the_masked_url()
    {
        using var harness = new AuthHarness();
        var (context, history, clock) = await SetUpAsync(harness);
        using var _ = context;
        history.Add(HistoryFixtures.Entry(clock, 5, url: "https://api.example.test/items"));

        var cut = HistoryPageHarness.RenderAt<HistoryPage>(context, HistoryLinks.List(null, 1));
        cut.Find("[data-testid='history-filter-text']").Change("items");
        cut.Find("[data-testid='history-filter']").Submit();

        Assert.EndsWith("/history?q=items", context.Services.GetRequiredService<NavigationManager>().Uri, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Deleting_one_entry_needs_a_second_confirmation()
    {
        using var harness = new AuthHarness();
        var (context, history, clock) = await SetUpAsync(harness);
        using var _ = context;
        var first = history.Add(HistoryFixtures.Entry(clock, 5));
        history.Add(HistoryFixtures.Entry(clock, 9));

        var cut = HistoryPageHarness.RenderAt<HistoryPage>(context, HistoryLinks.List(null, 1, first.Id));
        cut.WaitForState(() => cut.FindAll("[data-testid='history-delete']").Count == 1);

        cut.Find("[data-testid='history-delete']").Click();
        Assert.Single(cut.FindAll("[data-testid='history-delete-confirm']"));
        cut.Find("[data-testid='history-delete-cancel']").Click();
        Assert.Equal(2, history.Entries.Count);
        Assert.Empty(cut.FindAll("[data-testid='history-delete-confirm']"));

        cut.Find("[data-testid='history-delete']").Click();
        cut.Find("[data-testid='history-delete-confirm']").Click();

        Assert.Single(history.Entries);
        Assert.DoesNotContain(history.Entries, entry => entry.Id == first.Id);
        Assert.Equal(HistoryMessages.Deleted, cut.Find("[data-testid='history-notice']").TextContent);
    }

    [Fact]
    public async Task Clearing_the_history_needs_a_second_confirmation_and_then_removes_every_entry()
    {
        using var harness = new AuthHarness();
        var (context, history, clock) = await SetUpAsync(harness);
        using var _ = context;
        history.Add(HistoryFixtures.Entry(clock, 5));
        history.Add(HistoryFixtures.Entry(clock, 9));
        history.Add(HistoryFixtures.Entry(clock, 12));

        var cut = HistoryPageHarness.RenderAt<HistoryPage>(context, HistoryLinks.List(null, 1));
        cut.WaitForState(() => cut.FindAll("[data-testid='history-clear']").Count == 1);

        cut.Find("[data-testid='history-clear']").Click();
        cut.Find("[data-testid='history-clear-cancel']").Click();
        Assert.Equal(3, history.Entries.Count);

        cut.Find("[data-testid='history-clear']").Click();
        cut.Find("[data-testid='history-clear-confirm']").Click();

        Assert.Empty(history.Entries);
        Assert.Equal(HistoryMessages.Cleared, cut.Find("[data-testid='history-notice']").TextContent);
        cut.WaitForState(() => cut.FindAll("[data-testid='history-empty']").Count == 1);
    }

    [Fact]
    public async Task An_entry_that_is_not_the_callers_is_not_found_and_says_so()
    {
        using var harness = new AuthHarness();
        var (context, _, _) = await SetUpAsync(harness);
        using var _ = context;

        var cut = HistoryPageHarness.RenderAt<HistoryPage>(context, HistoryLinks.List(null, 1, Guid.NewGuid()));

        cut.WaitForState(() => cut.FindAll("[data-testid='history-detail-missing']").Count == 1);
        Assert.Equal(HistoryMessages.NotFound, cut.Find("[data-testid='history-detail-missing']").TextContent);
        Assert.Empty(cut.FindAll("[data-testid='history-detail']"));
    }

    [Fact]
    public async Task An_unreachable_sender_shows_the_list_could_not_load()
    {
        using var harness = new AuthHarness();
        var (context, history, _) = await SetUpAsync(harness);
        using var _ = context;
        history.Unavailable = true;

        var cut = HistoryPageHarness.RenderAt<HistoryPage>(context, HistoryLinks.List(null, 1));

        cut.WaitForState(() => cut.FindAll("[data-testid='history-list-error']").Count == 1);
        Assert.Equal(HistoryMessages.LoadFailed, cut.Find("[data-testid='history-list-error']").TextContent);
    }
}
