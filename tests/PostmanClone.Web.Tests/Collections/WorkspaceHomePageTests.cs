using Bunit;
using PostmanClone.ServiceDefaults.Authentication;
using PostmanClone.Web.Components.Pages;
using PostmanClone.Web.Tests.Auth;
using PostmanClone.Web.Tests.Membership;

namespace PostmanClone.Web.Tests.Collections;

/// <summary>The workspace home (WP18 acceptance, bUnit): the tree, the errors, the actions, the tabs and the layout pin.</summary>
public sealed class WorkspaceHomePageTests
{
    private static CancellationToken Ct => Xunit.TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_collection_opens_into_its_folders_and_requests_with_their_depth()
    {
        using var harness = new AuthHarness();
        var principal = await MembershipPageTestHarness.SignedInAsync(harness, Guid.NewGuid(), WorkspaceRoles.Owner, Ct);
        var fake = new FakeCollectionsService(Guid.NewGuid());
        var collection = fake.AddCollection("Billing");
        var folder = fake.AddFolder(collection, "Invoices");
        var nested = fake.AddFolder(collection, "Paid", parent: folder);
        var request = fake.AddRequest(collection, "List paid", folder: nested, method: "POST");
        using var context = CollectionsPageHarness.Create(harness, principal, fake);

        var cut = context.Render<WorkspaceHome>();
        cut.WaitForState(() => cut.FindAll($"[data-testid='tree-collection-{collection}']").Count == 1);
        Assert.Empty(cut.FindAll($"[data-testid='tree-folder-{folder}']"));

        cut.Find($"[data-testid='tree-toggle-{collection}']").Click();
        cut.WaitForState(() => cut.FindAll($"[data-testid='tree-folder-{folder}']").Count == 1);
        cut.Find($"[data-testid='tree-toggle-{folder}']").Click();
        cut.WaitForState(() => cut.FindAll($"[data-testid='tree-folder-{nested}']").Count == 1);
        cut.Find($"[data-testid='tree-toggle-{nested}']").Click();
        cut.WaitForState(() => cut.FindAll($"[data-testid='tree-request-{request}']").Count == 1);

        var requestRow = cut.Find($"[data-testid='tree-request-{request}']");
        Assert.Contains("POST", requestRow.TextContent);
        Assert.Contains("List paid", requestRow.TextContent);
        // The collection is level 1, its folder 2, the nested folder 3 and the request 4: the view draws the depth from aria-level.
        Assert.Equal("4", requestRow.Closest("li")!.GetAttribute("aria-level"));
        Assert.Equal("true", cut.Find($"[data-testid='tree-toggle-{folder}']").GetAttribute("aria-expanded"));
    }

    [Fact]
    public async Task A_failed_list_is_an_alert_and_the_shell_stays()
    {
        using var harness = new AuthHarness();
        var principal = await MembershipPageTestHarness.SignedInAsync(harness, Guid.NewGuid(), WorkspaceRoles.Member, Ct);
        var fake = new FakeCollectionsService(Guid.NewGuid()) { Unavailable = true };
        using var context = CollectionsPageHarness.Create(harness, principal, fake);

        var cut = context.Render<WorkspaceHome>();

        cut.WaitForState(() => cut.FindAll("[data-testid='shell-alert']").Count == 1);
        Assert.Contains("could not be loaded", cut.Find("[data-testid='shell-alert']").TextContent);
        Assert.Equal("alert", cut.Find("[data-testid='shell-alert']").GetAttribute("role"));
        Assert.Equal("Acme", cut.Find("[data-testid='page-title']").TextContent);
        Assert.Single(cut.FindAll("[data-testid='tree-new-collection']"));
        Assert.Single(cut.FindAll("[data-testid='actions']"));
        Assert.Empty(cut.FindAll("[data-testid='tree-empty']"));
    }

    [Fact]
    public async Task An_empty_workspace_says_so_and_offers_a_first_collection()
    {
        using var harness = new AuthHarness();
        var principal = await MembershipPageTestHarness.SignedInAsync(harness, Guid.NewGuid(), WorkspaceRoles.Owner, Ct);
        var fake = new FakeCollectionsService(Guid.NewGuid());
        using var context = CollectionsPageHarness.Create(harness, principal, fake);

        var cut = context.Render<WorkspaceHome>();

        cut.WaitForState(() => cut.FindAll("[data-testid='tree-empty']").Count == 1);
        Assert.Contains("No collections yet", cut.Find("[data-testid='tree-empty']").TextContent);
        Assert.Empty(cut.FindAll("[role='alert']"));
    }

    [Fact]
    public async Task One_collection_that_will_not_load_is_an_alert_on_its_row_and_the_others_still_show()
    {
        using var harness = new AuthHarness();
        var principal = await MembershipPageTestHarness.SignedInAsync(harness, Guid.NewGuid(), WorkspaceRoles.Owner, Ct);
        var fake = new FakeCollectionsService(Guid.NewGuid());
        var broken = fake.AddCollection("Broken");
        var healthy = fake.AddCollection("Healthy");
        fake.AddRequest(healthy, "Still here");
        fake.FailingTrees.Add(broken);
        using var context = CollectionsPageHarness.Create(harness, principal, fake);

        var cut = context.Render<WorkspaceHome>();
        cut.WaitForState(() => cut.FindAll($"[data-testid='tree-collection-{broken}']").Count == 1);
        cut.Find($"[data-testid='tree-toggle-{broken}']").Click();

        cut.WaitForState(() => cut.FindAll($"[data-testid='tree-error-{broken}']").Count == 1);
        Assert.Contains("could not be loaded", cut.Find($"[data-testid='tree-error-{broken}']").TextContent);
        Assert.Equal("alert", cut.Find($"[data-testid='tree-error-{broken}']").GetAttribute("role"));
        Assert.Single(cut.FindAll($"[data-testid='tree-collection-{healthy}']"));
        Assert.Single(cut.FindAll("[data-testid='actions']"));
    }

    [Fact]
    public async Task Deleting_asks_first_and_cancelling_keeps_the_collection()
    {
        using var harness = new AuthHarness();
        var principal = await MembershipPageTestHarness.SignedInAsync(harness, Guid.NewGuid(), WorkspaceRoles.Owner, Ct);
        var fake = new FakeCollectionsService(Guid.NewGuid());
        var collection = fake.AddCollection("Billing");
        fake.AddRequest(collection, "Refund");
        using var context = CollectionsPageHarness.Create(harness, principal, fake);
        var cut = context.Render<WorkspaceHome>();
        cut.WaitForState(() => cut.FindAll($"[data-testid='tree-collection-{collection}']").Count == 1);

        cut.Find($"[data-testid='tree-collection-{collection}']").Click();
        cut.WaitForState(() => cut.FindAll("[data-testid='action-delete']").Count == 1);
        cut.Find("[data-testid='action-delete']").Click();
        cut.WaitForState(() => cut.FindAll("[data-testid='delete-confirm']").Count == 1);
        Assert.Contains("1 request", cut.Find("[data-testid='delete-confirm']").TextContent);

        cut.Find("[data-testid='action-cancel']").Click();
        cut.WaitForState(() => cut.FindAll("[data-testid='delete-confirm']").Count == 0);
        Assert.True(fake.HasCollection(collection));
        Assert.DoesNotContain("DELETE", string.Join(' ', fake.Calls));
    }

    [Fact]
    public async Task Confirmed_delete_removes_the_collection_and_its_open_tabs()
    {
        using var harness = new AuthHarness();
        var principal = await MembershipPageTestHarness.SignedInAsync(harness, Guid.NewGuid(), WorkspaceRoles.Owner, Ct);
        var fake = new FakeCollectionsService(Guid.NewGuid());
        var collection = fake.AddCollection("Billing");
        var refund = fake.AddRequest(collection, "Refund");
        using var context = CollectionsPageHarness.Create(harness, principal, fake);
        var cut = context.Render<WorkspaceHome>();
        cut.WaitForState(() => cut.FindAll($"[data-testid='tree-collection-{collection}']").Count == 1);
        cut.Find($"[data-testid='tree-toggle-{collection}']").Click();
        cut.WaitForState(() => cut.FindAll($"[data-testid='tree-request-{refund}']").Count == 1);
        cut.Find($"[data-testid='tree-request-{refund}']").Click();
        cut.WaitForState(() => cut.FindAll("[data-testid='tabs-tab-1']").Count == 1);
        cut.Find($"[data-testid='tree-collection-{collection}']").Click();
        cut.WaitForState(() => cut.FindAll("[data-testid='action-delete']").Count == 1);

        cut.Find("[data-testid='action-delete']").Click();
        cut.WaitForState(() => cut.FindAll("[data-testid='action-confirm-delete']").Count == 1);
        cut.Find("[data-testid='action-confirm-delete']").Click();

        cut.WaitForState(() => cut.FindAll($"[data-testid='tree-collection-{collection}']").Count == 0);
        Assert.False(fake.HasCollection(collection));
        Assert.Empty(cut.FindAll("[data-testid='tabs-tab-1']"));
        Assert.Contains("Deleted", cut.Find("[data-testid='shell-notice']").TextContent);
    }

    [Fact]
    public async Task A_member_is_not_offered_delete_on_a_collection_someone_else_created()
    {
        using var harness = new AuthHarness();
        var principal = await MembershipPageTestHarness.SignedInAsync(harness, Guid.NewGuid(), WorkspaceRoles.Member, Ct);
        var fake = new FakeCollectionsService(Guid.NewGuid());
        var collection = fake.AddCollection("Someone else's", createdBy: Guid.NewGuid());
        using var context = CollectionsPageHarness.Create(harness, principal, fake);
        var cut = context.Render<WorkspaceHome>();
        cut.WaitForState(() => cut.FindAll($"[data-testid='tree-collection-{collection}']").Count == 1);

        cut.Find($"[data-testid='tree-collection-{collection}']").Click();

        cut.WaitForState(() => cut.FindAll("[data-testid='selection-name']").Count == 1);
        Assert.Empty(cut.FindAll("[data-testid='action-delete']"));
        Assert.Single(cut.FindAll("[data-testid='action-rename']"));
    }

    [Fact]
    public async Task A_refused_delete_says_so_and_keeps_the_item()
    {
        using var harness = new AuthHarness();
        var principal = await MembershipPageTestHarness.SignedInAsync(harness, Guid.NewGuid(), WorkspaceRoles.Owner, Ct);
        var fake = new FakeCollectionsService(Guid.NewGuid()) { ForbidCollectionDelete = true };
        var collection = fake.AddCollection("Billing");
        using var context = CollectionsPageHarness.Create(harness, principal, fake);
        var cut = context.Render<WorkspaceHome>();
        cut.WaitForState(() => cut.FindAll($"[data-testid='tree-collection-{collection}']").Count == 1);
        cut.Find($"[data-testid='tree-collection-{collection}']").Click();
        cut.WaitForState(() => cut.FindAll("[data-testid='action-delete']").Count == 1);

        cut.Find("[data-testid='action-delete']").Click();
        cut.WaitForState(() => cut.FindAll("[data-testid='action-confirm-delete']").Count == 1);
        cut.Find("[data-testid='action-confirm-delete']").Click();

        cut.WaitForState(() => cut.FindAll("[data-testid='shell-notice']").Count == 1);
        Assert.Contains("do not have permission", cut.Find("[data-testid='shell-notice']").TextContent);
        Assert.Equal("alert", cut.Find("[data-testid='shell-notice']").GetAttribute("role"));
        Assert.True(fake.HasCollection(collection));
    }

    [Fact]
    public async Task Typing_in_the_filter_keeps_the_matches_with_their_ancestors_and_reads_every_tree_once()
    {
        using var harness = new AuthHarness();
        var principal = await MembershipPageTestHarness.SignedInAsync(harness, Guid.NewGuid(), WorkspaceRoles.Owner, Ct);
        var fake = new FakeCollectionsService(Guid.NewGuid());
        var billing = fake.AddCollection("Billing");
        var invoices = fake.AddFolder(billing, "Invoices");
        var refund = fake.AddRequest(billing, "Refund flow", folder: invoices);
        fake.AddRequest(billing, "Unrelated", folder: invoices);
        var auth = fake.AddCollection("Auth");
        var login = fake.AddRequest(auth, "Login");
        using var context = CollectionsPageHarness.Create(harness, principal, fake);
        var cut = context.Render<WorkspaceHome>();
        cut.WaitForState(() => cut.FindAll($"[data-testid='tree-collection-{auth}']").Count == 1);

        cut.Find("[data-testid='tree-filter']").Input("refund");

        cut.WaitForState(() => cut.FindAll($"[data-testid='tree-request-{refund}']").Count == 1);
        Assert.Single(cut.FindAll($"[data-testid='tree-collection-{billing}']"));
        Assert.Single(cut.FindAll($"[data-testid='tree-folder-{invoices}']"));
        Assert.Empty(cut.FindAll($"[data-testid='tree-collection-{auth}']"));
        Assert.Empty(cut.FindAll($"[data-testid='tree-request-{login}']"));
        // Each tree is read once: the filter loads the trees it needs, and a loaded tree is kept.
        Assert.Equal(1, fake.Calls.Count(call => call == $"GET /api/v1/collections/{billing}"));
        Assert.Equal(1, fake.Calls.Count(call => call == $"GET /api/v1/collections/{auth}"));

        cut.Find("[data-testid='tree-filter']").Input("zzz-nothing");
        cut.WaitForState(() => cut.FindAll("[data-testid='tree-filter-empty']").Count == 1);
        Assert.Contains("zzz-nothing", cut.Find("[data-testid='tree-filter-empty']").TextContent);
    }

    [Fact]
    public async Task A_new_collection_is_created_and_shown()
    {
        using var harness = new AuthHarness();
        var principal = await MembershipPageTestHarness.SignedInAsync(harness, Guid.NewGuid(), WorkspaceRoles.Owner, Ct);
        var fake = new FakeCollectionsService(Guid.NewGuid());
        using var context = CollectionsPageHarness.Create(harness, principal, fake);
        var cut = context.Render<WorkspaceHome>();
        cut.WaitForState(() => cut.FindAll("[data-testid='tree-new-collection']").Count == 1);

        cut.Find("[data-testid='tree-new-collection']").Click();
        cut.WaitForState(() => cut.FindAll("[data-testid='new-collection-form']").Count == 1);
        cut.Find("[data-testid='new-collection-name']").Input("Payments");
        cut.Find("[data-testid='new-collection-form']").Submit();

        cut.WaitForState(() => cut.FindAll("[data-testid='tree'] [data-kind='collection']").Count == 1);
        Assert.Contains("Payments", cut.Find("[data-testid='tree']").TextContent);
        Assert.Contains("Created the collection", cut.Find("[data-testid='shell-notice']").TextContent);
    }

    [Fact]
    public async Task A_blank_name_is_refused_on_the_page_without_a_call()
    {
        using var harness = new AuthHarness();
        var principal = await MembershipPageTestHarness.SignedInAsync(harness, Guid.NewGuid(), WorkspaceRoles.Owner, Ct);
        var fake = new FakeCollectionsService(Guid.NewGuid());
        using var context = CollectionsPageHarness.Create(harness, principal, fake);
        var cut = context.Render<WorkspaceHome>();
        cut.WaitForState(() => cut.FindAll("[data-testid='tree-new-collection']").Count == 1);
        cut.Find("[data-testid='tree-new-collection']").Click();
        cut.WaitForState(() => cut.FindAll("[data-testid='new-collection-form']").Count == 1);

        cut.Find("[data-testid='new-collection-name']").Input("   ");
        cut.Find("[data-testid='new-collection-form']").Submit();

        cut.WaitForState(() => cut.FindAll("[data-testid='form-error']").Count == 1);
        Assert.Contains("Give the collection a name", cut.Find("[data-testid='form-error']").TextContent);
        Assert.DoesNotContain("POST /api/v1/collections", fake.Calls);
    }

    [Fact]
    public async Task A_request_opens_in_a_tab_and_closing_it_leaves_the_placeholder()
    {
        using var harness = new AuthHarness();
        var principal = await MembershipPageTestHarness.SignedInAsync(harness, Guid.NewGuid(), WorkspaceRoles.Owner, Ct);
        var fake = new FakeCollectionsService(Guid.NewGuid());
        var collection = fake.AddCollection("Billing");
        var refund = fake.AddRequest(collection, "Refund");
        using var context = CollectionsPageHarness.Create(harness, principal, fake);
        var cut = context.Render<WorkspaceHome>();
        cut.WaitForState(() => cut.FindAll($"[data-testid='tree-collection-{collection}']").Count == 1);
        cut.Find($"[data-testid='tree-toggle-{collection}']").Click();
        cut.WaitForState(() => cut.FindAll($"[data-testid='tree-request-{refund}']").Count == 1);

        cut.Find($"[data-testid='tree-request-{refund}']").Click();

        cut.WaitForState(() => cut.FindAll("[data-testid='tabs-tab-1']").Count == 1);
        Assert.Equal("Refund", cut.Find("[data-testid='request-title']").TextContent);
        Assert.Equal("true", cut.Find("[data-testid='tabs-tab-1']").GetAttribute("aria-selected"));

        cut.Find("[data-testid='tabs-close-1']").Click();

        cut.WaitForState(() => cut.FindAll("[data-testid='tabs-tab-1']").Count == 0);
        Assert.Single(cut.FindAll("[data-testid='request-empty']"));
    }

    [Fact]
    public async Task The_twenty_first_tab_is_refused_with_a_message()
    {
        using var harness = new AuthHarness();
        var principal = await MembershipPageTestHarness.SignedInAsync(harness, Guid.NewGuid(), WorkspaceRoles.Owner, Ct);
        var fake = new FakeCollectionsService(Guid.NewGuid());
        var collection = fake.AddCollection("Many");
        var requests = Enumerable.Range(1, 21).Select(index => fake.AddRequest(collection, $"Request {index:00}", sortOrder: index)).ToList();
        using var context = CollectionsPageHarness.Create(harness, principal, fake);
        var cut = context.Render<WorkspaceHome>();
        cut.WaitForState(() => cut.FindAll($"[data-testid='tree-collection-{collection}']").Count == 1);
        cut.Find($"[data-testid='tree-toggle-{collection}']").Click();
        cut.WaitForState(() => cut.FindAll($"[data-testid='tree-request-{requests[^1]}']").Count == 1);

        foreach (var request in requests.Take(20))
        {
            cut.Find($"[data-testid='tree-request-{request}']").Click();
        }

        cut.WaitForState(() => cut.FindAll("[data-testid^='tabs-tab-']").Count == 20);
        cut.Find($"[data-testid='tree-request-{requests[20]}']").Click();

        cut.WaitForState(() => cut.FindAll("[data-testid='shell-notice']").Count == 1);
        Assert.Equal(20, cut.FindAll("[data-testid^='tabs-tab-']").Count);
        Assert.Contains("Up to 20 request tabs", cut.Find("[data-testid='shell-notice']").TextContent);
    }

    [Fact]
    public async Task Duplicate_makes_a_copy_beside_the_original()
    {
        using var harness = new AuthHarness();
        var principal = await MembershipPageTestHarness.SignedInAsync(harness, Guid.NewGuid(), WorkspaceRoles.Owner, Ct);
        var fake = new FakeCollectionsService(Guid.NewGuid());
        var collection = fake.AddCollection("Billing");
        var refund = fake.AddRequest(collection, "Refund");
        using var context = CollectionsPageHarness.Create(harness, principal, fake);
        var cut = context.Render<WorkspaceHome>();
        cut.WaitForState(() => cut.FindAll($"[data-testid='tree-collection-{collection}']").Count == 1);
        cut.Find($"[data-testid='tree-toggle-{collection}']").Click();
        cut.WaitForState(() => cut.FindAll($"[data-testid='tree-request-{refund}']").Count == 1);
        cut.Find($"[data-testid='tree-request-{refund}']").Click();
        cut.WaitForState(() => cut.FindAll("[data-testid='action-duplicate']").Count == 1);

        cut.Find("[data-testid='action-duplicate']").Click();

        cut.WaitForState(() => cut.Markup.Contains("Refund copy"));
        Assert.Single(fake.Calls, call => call.EndsWith("/duplicate", StringComparison.Ordinal));
        Assert.Contains("Duplicated as", cut.Find("[data-testid='shell-notice']").TextContent);
    }

    [Fact]
    public async Task A_request_moves_into_a_chosen_folder()
    {
        using var harness = new AuthHarness();
        var principal = await MembershipPageTestHarness.SignedInAsync(harness, Guid.NewGuid(), WorkspaceRoles.Owner, Ct);
        var fake = new FakeCollectionsService(Guid.NewGuid());
        var collection = fake.AddCollection("Billing");
        var folder = fake.AddFolder(collection, "Archive");
        var refund = fake.AddRequest(collection, "Refund");
        using var context = CollectionsPageHarness.Create(harness, principal, fake);
        var cut = context.Render<WorkspaceHome>();
        cut.WaitForState(() => cut.FindAll($"[data-testid='tree-collection-{collection}']").Count == 1);
        cut.Find($"[data-testid='tree-toggle-{collection}']").Click();
        cut.WaitForState(() => cut.FindAll($"[data-testid='tree-request-{refund}']").Count == 1);
        cut.Find($"[data-testid='tree-request-{refund}']").Click();
        cut.WaitForState(() => cut.FindAll("[data-testid='action-move']").Count == 1);

        cut.Find("[data-testid='action-move']").Click();
        cut.WaitForState(() => cut.FindAll("[data-testid='move-form']").Count == 1);
        var options = cut.FindAll("[data-testid='move-target'] option").Select(option => option.TextContent).ToArray();
        Assert.Equal(["Top of the collection", "Archive"], options);
        cut.Find("[data-testid='move-target']").Change(folder.ToString());
        cut.Find("[data-testid='move-form']").Submit();

        cut.WaitForState(() => cut.FindAll("[data-testid='move-form']").Count == 0);
        Assert.Equal(folder, fake.RequestFolder(refund));
        Assert.Contains("Moved", cut.Find("[data-testid='shell-notice']").TextContent);
    }

    [Fact]
    public async Task A_renamed_request_keeps_its_open_tab_under_the_new_name()
    {
        using var harness = new AuthHarness();
        var principal = await MembershipPageTestHarness.SignedInAsync(harness, Guid.NewGuid(), WorkspaceRoles.Owner, Ct);
        var fake = new FakeCollectionsService(Guid.NewGuid());
        var collection = fake.AddCollection("Billing");
        var refund = fake.AddRequest(collection, "Refund");
        using var context = CollectionsPageHarness.Create(harness, principal, fake);
        var cut = context.Render<WorkspaceHome>();
        cut.WaitForState(() => cut.FindAll($"[data-testid='tree-collection-{collection}']").Count == 1);
        cut.Find($"[data-testid='tree-toggle-{collection}']").Click();
        cut.WaitForState(() => cut.FindAll($"[data-testid='tree-request-{refund}']").Count == 1);
        cut.Find($"[data-testid='tree-request-{refund}']").Click();
        cut.WaitForState(() => cut.FindAll("[data-testid='action-rename']").Count == 1);

        cut.Find("[data-testid='action-rename']").Click();
        cut.WaitForState(() => cut.FindAll("[data-testid='rename-form']").Count == 1);
        cut.Find("[data-testid='rename-name']").Input("Refund (v2)");
        cut.Find("[data-testid='rename-form']").Submit();

        cut.WaitForState(() => cut.FindAll("[data-testid='rename-form']").Count == 0);
        Assert.Equal("Refund (v2)", fake.RequestName(refund));
        Assert.Equal("Refund (v2)", cut.Find("[data-testid='tabs-tab-1']").TextContent.Trim());
        Assert.Equal(2, fake.RequestVersion(refund));
    }

    [Fact]
    public async Task No_token_reaches_the_markup()
    {
        using var harness = new AuthHarness();
        var principal = await MembershipPageTestHarness.SignedInAsync(harness, Guid.NewGuid(), WorkspaceRoles.Owner, Ct);
        var fake = new FakeCollectionsService(Guid.NewGuid());
        var collection = fake.AddCollection("Billing");
        fake.AddRequest(collection, "Refund");
        using var context = CollectionsPageHarness.Create(harness, principal, fake);
        var cut = context.Render<WorkspaceHome>();
        cut.WaitForState(() => cut.FindAll($"[data-testid='tree-collection-{collection}']").Count == 1);
        cut.Find($"[data-testid='tree-toggle-{collection}']").Click();
        cut.WaitForState(() => cut.FindAll("[data-testid^='tree-request-']").Count == 1);

        var bearer = fake.LastBearer;
        Assert.False(string.IsNullOrEmpty(bearer));
        Assert.DoesNotContain(bearer!, cut.Markup);
        Assert.DoesNotContain(harness.Auth.LastRefreshToken ?? "no-refresh-token", cut.Markup);
        Assert.DoesNotContain("eyJ", cut.Markup);
    }

    [Fact]
    public async Task The_tree_sits_in_a_scrolling_box_so_a_phone_does_not_overflow()
    {
        using var harness = new AuthHarness();
        var principal = await MembershipPageTestHarness.SignedInAsync(harness, Guid.NewGuid(), WorkspaceRoles.Owner, Ct);
        var fake = new FakeCollectionsService(Guid.NewGuid());
        fake.AddCollection("Billing");
        using var context = CollectionsPageHarness.Create(harness, principal, fake);

        var cut = context.Render<WorkspaceHome>();

        cut.WaitForState(() => cut.FindAll("[data-testid='tree']").Count == 1);
        Assert.NotNull(cut.Find("[data-testid='tree']").Closest(".table-wrap"));
        Assert.Single(cut.FindAll(".workspace-grid"));
    }
}
