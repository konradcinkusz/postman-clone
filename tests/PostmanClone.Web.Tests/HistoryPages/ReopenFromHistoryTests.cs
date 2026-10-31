using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using PostmanClone.ServiceDefaults.Authentication;
using PostmanClone.Web.Api;
using PostmanClone.Web.Auth;
using PostmanClone.Web.Components.Pages;
using PostmanClone.Web.History;
using PostmanClone.Web.Response;
using PostmanClone.Web.Tests.Auth;
using PostmanClone.Web.Tests.Collections;
using PostmanClone.Web.Tests.History;
using PostmanClone.Web.Tests.Membership;
using static PostmanClone.Web.Tests.Response.ResponseViewTests;

namespace PostmanClone.Web.Tests.HistoryPages;

/// <summary>
/// "Open in builder" on the workspace home (WP22, ADR-0015): the entry opens as a new unsaved draft, the masked values stay masked and
/// stop the send and the save, a clean entry sends without a saved request, and a send that was recorded refreshes the history panel.
/// </summary>
public sealed class ReopenFromHistoryTests
{
    private static CancellationToken Ct => Xunit.TestContext.Current.CancellationToken;

    /// <summary>Records every body the page sends and answers with the given result.</summary>
    private sealed class RecordingGateway(Func<ExecuteBody, CollectionAnswer<ExecutionResultDto>> respond) : IExecutionGateway
    {
        public List<ExecuteBody> Bodies { get; } = [];

        public Task<CollectionAnswer<ExecutionResultDto>> ExecuteAsync(ExecuteBody body, CancellationToken cancellationToken)
        {
            Bodies.Add(body);
            return Task.FromResult(respond(body));
        }
    }

    private static async Task<(BunitContext Context, RecordingGateway Gateway)> SetUpAsync(
        AuthHarness harness,
        FakeHistoryClient history,
        FakeTimeProvider clock,
        Func<ExecuteBody, CollectionAnswer<ExecutionResultDto>>? respond = null)
    {
        var principal = await MembershipPageTestHarness.SignedInAsync(harness, Guid.NewGuid(), WorkspaceRoles.Owner, Ct);
        var collections = new FakeCollectionsService(Guid.NewGuid());
        var gateway = new RecordingGateway(respond ?? (_ => new CollectionAnswer<ExecutionResultDto>(CollectionOutcome.Succeeded, Result(), null)));
        var state = HistoryPageHarness.Applied(principal);
        var session = new CircuitSession(state, harness.Refresher);
        var context = CollectionsPageHarness.Create(harness, principal, collections, gateway, state, session, history, clock);
        return (context, gateway);
    }

    [Fact]
    public async Task Opening_an_entry_makes_an_unsaved_draft_that_cannot_be_saved_by_id()
    {
        using var harness = new AuthHarness();
        var clock = new FakeTimeProvider();
        var history = new FakeHistoryClient();
        var (context, _) = await SetUpAsync(harness, history, clock);
        using var _ = context;
        var entry = history.Add(HistoryFixtures.Entry(clock, 5, maskedHeader: true));

        var cut = HistoryPageHarness.RenderAt<WorkspaceHome>(context, HistoryLinks.OpenInBuilder(entry.Id));

        cut.WaitForState(() => cut.FindAll("[data-testid='request-title']").Count == 1);
        Assert.Equal(HistoryReopen.TitleFor("GET", entry.Request.Url), cut.Find("[data-testid='request-title']").TextContent);
        Assert.True(cut.Find("[data-testid='builder-save']").HasAttribute("disabled"));
        Assert.Single(cut.FindAll("[data-testid='builder-save-as']"));
        Assert.Single(cut.FindAll("[data-testid='builder-dirty']"));
        Assert.Equal(HistoryMessages.Reopened, cut.Find("[data-testid='shell-notice']").TextContent);
    }

    [Fact]
    public async Task A_draft_that_still_holds_the_mask_is_not_sent()
    {
        using var harness = new AuthHarness();
        var clock = new FakeTimeProvider();
        var history = new FakeHistoryClient();
        var (context, gateway) = await SetUpAsync(harness, history, clock);
        using var _ = context;
        var entry = history.Add(HistoryFixtures.Entry(clock, 5, maskedHeader: true));
        var cut = HistoryPageHarness.RenderAt<WorkspaceHome>(context, HistoryLinks.OpenInBuilder(entry.Id));
        cut.WaitForState(() => cut.FindAll("[data-testid='builder-send']").Count == 1);

        cut.Find("[data-testid='builder-send']").Click();

        Assert.Empty(gateway.Bodies);
        Assert.Equal(HistoryMessages.MaskedBeforeSend, cut.Find("[data-testid='shell-notice']").TextContent);
    }

    [Fact]
    public async Task A_clean_entry_is_sent_as_a_draft_with_no_saved_request_and_its_references_kept()
    {
        using var harness = new AuthHarness();
        var clock = new FakeTimeProvider();
        var history = new FakeHistoryClient();
        var (context, gateway) = await SetUpAsync(harness, history, clock);
        using var _ = context;
        var entry = history.Add(HistoryFixtures.Entry(clock, 5, url: "https://api.example.test/items/{{itemId}}"));
        var cut = HistoryPageHarness.RenderAt<WorkspaceHome>(context, HistoryLinks.OpenInBuilder(entry.Id));
        cut.WaitForState(() => cut.FindAll("[data-testid='builder-send']").Count == 1);

        cut.Find("[data-testid='builder-send']").Click();

        var body = Assert.Single(gateway.Bodies);
        Assert.Null(body.SavedRequestId);
        Assert.Equal("https://api.example.test/items/{{itemId}}", body.Request.Url);
    }

    [Fact]
    public async Task A_send_that_is_recorded_refreshes_the_history_panel()
    {
        using var harness = new AuthHarness();
        var clock = new FakeTimeProvider();
        var history = new FakeHistoryClient();
        var seed = history.Add(HistoryFixtures.Entry(clock, 9, url: "https://api.example.test/seed"));
        HistoryEntryDto? recorded = null;

        var (context, _) = await SetUpAsync(harness, history, clock, body =>
        {
            // The sender records the send before it answers, as ExecutionService does (ADR-0015).
            recorded = history.Add(HistoryFixtures.Entry(clock, 0, url: body.Request.Url));
            return new CollectionAnswer<ExecutionResultDto>(CollectionOutcome.Succeeded, Result() with { HistoryId = recorded.Id }, null);
        });
        using var _ = context;
        var cut = HistoryPageHarness.RenderAt<WorkspaceHome>(context, HistoryLinks.OpenInBuilder(seed.Id));
        cut.WaitForState(() => cut.FindAll($"[data-testid='history-panel-item-{seed.Id}']").Count == 1);

        cut.Find("[data-testid='builder-send']").Click();

        cut.WaitForState(() => cut.FindAll("[data-testid^='history-panel-item-']").Count == 2);
        Assert.NotNull(recorded);
        Assert.NotEmpty(cut.FindAll($"[data-testid='history-panel-item-{recorded!.Id}']"));
    }
}
