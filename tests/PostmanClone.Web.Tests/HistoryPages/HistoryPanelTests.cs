using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using PostmanClone.ServiceDefaults.Authentication;
using PostmanClone.Web.History;
using PostmanClone.Web.Components.Shell;
using PostmanClone.Web.Tests.Auth;
using PostmanClone.Web.Tests.History;
using PostmanClone.Web.Tests.Membership;

namespace PostmanClone.Web.Tests.HistoryPages;

/// <summary>
/// The workspace shell's history panel (WP22): the newest entries, an open that hands the entry to the page, a reload when the
/// circuit's history changes, and no call at all once the panel is disposed (the subscription is released).
/// </summary>
public sealed class HistoryPanelTests
{
    private static CancellationToken Ct => Xunit.TestContext.Current.CancellationToken;

    private static async Task<(BunitContext Context, FakeHistoryClient History, FakeTimeProvider Clock, HistoryChangeSignal Signal)> SetUpAsync(AuthHarness harness)
    {
        var principal = await MembershipPageTestHarness.SignedInAsync(harness, Guid.NewGuid(), WorkspaceRoles.Member, Ct);
        var clock = new FakeTimeProvider();
        var history = new FakeHistoryClient();
        var context = HistoryPageHarness.Create(harness, principal, history, clock);
        return (context, history, clock, context.Services.GetRequiredService<HistoryChangeSignal>());
    }

    [Fact]
    public async Task It_shows_the_ten_newest_entries_and_no_more()
    {
        using var harness = new AuthHarness();
        var (context, history, clock, _) = await SetUpAsync(harness);
        using var _ = context;
        for (var minutes = 1; minutes <= 12; minutes++)
        {
            history.Add(HistoryFixtures.Entry(clock, minutes));
        }

        var cut = context.Render<HistoryPanel>(parameters => parameters.Add(panel => panel.OnOpen, EventCallback.Factory.Create<Guid>(this, _ => { })));

        cut.WaitForState(() => cut.FindAll("[data-testid^='history-panel-item-']").Count > 0);
        Assert.Equal(HistoryPaging.PanelSize, cut.FindAll("[data-testid^='history-panel-item-']").Count);
        Assert.Equal(history.Entries.OrderByDescending(entry => entry.ExecutedAt).First().Id, ParseId(cut.FindAll("[data-testid^='history-panel-item-']")[0].GetAttribute("data-testid")!));
    }

    [Fact]
    public async Task Opening_an_entry_hands_its_id_to_the_page()
    {
        using var harness = new AuthHarness();
        var (context, history, clock, _) = await SetUpAsync(harness);
        using var _ = context;
        var entry = history.Add(HistoryFixtures.Entry(clock, 5));
        Guid? opened = null;

        var cut = context.Render<HistoryPanel>(parameters => parameters.Add(panel => panel.OnOpen, EventCallback.Factory.Create<Guid>(this, id => opened = id)));
        cut.WaitForState(() => cut.FindAll($"[data-testid='history-panel-open-{entry.Id}']").Count == 1);
        cut.Find($"[data-testid='history-panel-open-{entry.Id}']").Click();

        Assert.Equal(entry.Id, opened);
    }

    [Fact]
    public async Task A_recorded_send_reloads_the_panel_through_the_change_signal()
    {
        using var harness = new AuthHarness();
        var (context, history, clock, signal) = await SetUpAsync(harness);
        using var _ = context;
        history.Add(HistoryFixtures.Entry(clock, 9));

        var cut = context.Render<HistoryPanel>(parameters => parameters.Add(panel => panel.OnOpen, EventCallback.Factory.Create<Guid>(this, _ => { })));
        cut.WaitForState(() => cut.FindAll("[data-testid^='history-panel-item-']").Count == 1);

        var recorded = history.Add(HistoryFixtures.Entry(clock, 0));
        signal.Raise();

        cut.WaitForState(() => cut.FindAll("[data-testid^='history-panel-item-']").Count == 2);
        Assert.NotEmpty(cut.FindAll($"[data-testid='history-panel-item-{recorded.Id}']"));
    }

    [Fact]
    public async Task After_the_panel_is_disposed_a_change_reaches_nobody()
    {
        using var harness = new AuthHarness();
        var (context, history, clock, signal) = await SetUpAsync(harness);
        history.Add(HistoryFixtures.Entry(clock, 5));
        var cut = context.Render<HistoryPanel>(parameters => parameters.Add(panel => panel.OnOpen, EventCallback.Factory.Create<Guid>(this, _ => { })));
        cut.WaitForState(() => cut.FindAll("[data-testid^='history-panel-item-']").Count == 1);

        cut.Instance.Dispose();
        var callsBefore = history.Calls.Count;
        signal.Raise();

        Assert.Equal(callsBefore, history.Calls.Count);
        context.Dispose();
    }

    [Fact]
    public async Task An_unreachable_sender_says_so_in_the_panel()
    {
        using var harness = new AuthHarness();
        var (context, history, _, _) = await SetUpAsync(harness);
        using var _ = context;
        history.Unavailable = true;

        var cut = context.Render<HistoryPanel>(parameters => parameters.Add(panel => panel.OnOpen, EventCallback.Factory.Create<Guid>(this, _ => { })));

        cut.WaitForState(() => cut.FindAll("[data-testid='history-panel-error']").Count == 1);
        Assert.Equal(HistoryMessages.LoadFailed, cut.Find("[data-testid='history-panel-error']").TextContent);
    }

    private static Guid ParseId(string testId) => Guid.Parse(testId["history-panel-item-".Length..]);
}
