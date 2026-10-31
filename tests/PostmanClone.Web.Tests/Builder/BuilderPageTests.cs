using Bunit;
using PostmanClone.ServiceDefaults.Authentication;
using PostmanClone.Web.Api;
using PostmanClone.Web.Builder;
using PostmanClone.Web.Components.Pages;
using PostmanClone.Web.Tests.Auth;
using PostmanClone.Web.Tests.Collections;
using PostmanClone.Web.Tests.Membership;

namespace PostmanClone.Web.Tests.Builder;

/// <summary>
/// The request builder on the workspace home (WP19 acceptance, bUnit): what opens, what a change does to the URL, the table and the
/// tab, every save outcome (saved, refused on the page, refused by the service, conflict), save as, the new request, and the body
/// and auth editors. The service is the fake behind the real client, so each assertion reads what the service would have stored.
/// </summary>
public sealed class BuilderPageTests
{
    private static CancellationToken Ct => Xunit.TestContext.Current.CancellationToken;

    private static RequestDefinition Definition(string url = "https://api.example.test/items", params KeyValue[] parameters) =>
        new("GET", url, parameters.ToList(), [], new RequestBody("none", null, null, []), new RequestAuth("none", null, null, null, null, null, null));

    private static async Task<(BunitContext Context, IRenderedComponent<WorkspaceHome> Page, FakeCollectionsService Fake, Guid Collection, Guid Request)> SeededAsync(
        AuthHarness harness, Guid? requestId = null, RequestDefinition? definition = null)
    {
        var principal = await MembershipPageTestHarness.SignedInAsync(harness, Guid.NewGuid(), WorkspaceRoles.Owner, Ct);
        var fake = new FakeCollectionsService(Guid.NewGuid());
        var collection = fake.AddCollection("Billing");
        var request = fake.AddRequest(collection, "List items", definition: definition ?? Definition("https://api.example.test/items", new KeyValue("page", "2")));
        var context = CollectionsPageHarness.Create(harness, principal, fake);
        return (context, Render(context, collection, request), fake, collection, request);
    }

    private static IRenderedComponent<WorkspaceHome> Render(BunitContext context, Guid collection, Guid request)
    {
        var cut = context.Render<WorkspaceHome>();
        cut.WaitForState(() => cut.FindAll($"[data-testid='tree-collection-{collection}']").Count == 1);
        cut.Find($"[data-testid='tree-toggle-{collection}']").Click();
        cut.WaitForState(() => cut.FindAll($"[data-testid='tree-request-{request}']").Count == 1);
        cut.Find($"[data-testid='tree-request-{request}']").Click();
        cut.WaitForState(() => cut.FindAll("[data-testid='builder-url']").Count == 1);
        return cut;
    }

    private static string UrlText(IRenderedComponent<WorkspaceHome> cut) => cut.Find("[data-testid='builder-url']").GetAttribute("value") ?? string.Empty;

    private static void Save(IRenderedComponent<WorkspaceHome> cut)
    {
        cut.Find("[data-testid='builder-save']").Click();
    }

    [Fact]
    public async Task An_opened_request_shows_its_method_its_url_and_its_rows()
    {
        using var harness = new AuthHarness();
        var (context, cut, _, _, _) = await SeededAsync(harness);
        using var _ = context;

        Assert.Equal("https://api.example.test/items?page=2", UrlText(cut));
        Assert.Equal("GET", cut.Find("[data-testid='builder-method'] option[selected]").TextContent);
        Assert.Equal("page", cut.Find("[data-testid='kv-key-0']").GetAttribute("value"));
        Assert.Equal("2", cut.Find("[data-testid='kv-value-0']").GetAttribute("value"));
        Assert.Single(cut.FindAll("[data-testid='builder-clean']"));
    }

    [Fact]
    public async Task Typing_in_the_url_fills_the_table_and_marks_the_tab_dirty()
    {
        using var harness = new AuthHarness();
        var (context, cut, _, _, _) = await SeededAsync(harness);
        using var _ = context;

        cut.Find("[data-testid='builder-url']").Input("https://api.example.test/items?page=3&q=ü");

        cut.WaitForState(() => cut.FindAll("[data-testid='kv-key-1']").Count == 1);
        Assert.Equal("q", cut.Find("[data-testid='kv-key-1']").GetAttribute("value"));
        Assert.Equal("https://api.example.test/items?page=3&q=ü", UrlText(cut));
        Assert.Single(cut.FindAll("[data-testid='builder-dirty']"));
        Assert.Contains("●", cut.Find("[data-testid='tabs-tab-1']").TextContent);
    }

    [Fact]
    public async Task Editing_a_row_rewrites_the_url_and_saving_stores_the_query_once_in_the_table()
    {
        using var harness = new AuthHarness();
        var (context, cut, fake, _, request) = await SeededAsync(harness);
        using var _ = context;

        cut.Find("[data-testid='kv-value-0']").Input("5");
        cut.WaitForState(() => UrlText(cut) == "https://api.example.test/items?page=5");
        Save(cut);

        cut.WaitForState(() => cut.FindAll("[data-testid='builder-clean']").Count == 1);
        var stored = fake.DefinitionOf(request)!;
        Assert.Equal("https://api.example.test/items", stored.Url);
        Assert.Equal("5", Assert.Single(stored.Params!).Value);
        Assert.Equal("Saved.", cut.Find("[data-testid='shell-notice']").TextContent);
    }

    [Fact]
    public async Task A_disabled_row_is_kept_in_the_table_and_out_of_the_url()
    {
        using var harness = new AuthHarness();
        var (context, cut, fake, _, request) = await SeededAsync(harness);
        using var _ = context;

        cut.Find("[data-testid='kv-enabled-0']").Change(false);
        cut.WaitForState(() => UrlText(cut) == "https://api.example.test/items");
        Save(cut);

        cut.WaitForState(() => cut.FindAll("[data-testid='builder-clean']").Count == 1);
        Assert.False(Assert.Single(fake.DefinitionOf(request)!.Params!).Enabled);
    }

    [Fact]
    public async Task A_literal_secret_is_refused_beside_its_field_and_nothing_is_sent()
    {
        using var harness = new AuthHarness();
        var (context, cut, fake, _, _) = await SeededAsync(harness);
        using var _ = context;

        cut.Find("[data-testid='builder-tab-auth']").Click();
        cut.Find("[data-testid='builder-auth-type']").Change("bearer");
        cut.WaitForState(() => cut.FindAll("[data-testid='auth-bearer']").Count == 1);
        cut.Find("[data-testid='auth-bearer']").Input("Bearer literal-token-value"); // gitleaks:allow test fixture, not a credential
        Save(cut);

        cut.WaitForState(() => cut.FindAll("[data-testid='shell-notice']").Count == 1);
        Assert.Equal("alert", cut.Find("[data-testid='shell-notice']").GetAttribute("role"));
        Assert.Contains("Not saved", cut.Find("[data-testid='shell-notice']").TextContent);
        Assert.Single(cut.FindAll("[data-testid='auth-bearer-refused']"));
        Assert.DoesNotContain(fake.Calls, call => call.StartsWith("PUT ", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_reference_in_a_secret_field_is_accepted_and_saved()
    {
        using var harness = new AuthHarness();
        var (context, cut, fake, _, request) = await SeededAsync(harness);
        using var _ = context;

        cut.Find("[data-testid='builder-tab-auth']").Click();
        cut.Find("[data-testid='builder-auth-type']").Change("bearer");
        cut.WaitForState(() => cut.FindAll("[data-testid='auth-bearer']").Count == 1);
        cut.Find("[data-testid='auth-bearer']").Input("{{token}}");
        Save(cut);

        cut.WaitForState(() => cut.FindAll("[data-testid='builder-clean']").Count == 1);
        Assert.Equal("{{token}}", fake.DefinitionOf(request)!.Auth!.BearerToken);
        Assert.Empty(cut.FindAll("[data-testid='auth-bearer-refused']"));
    }

    [Fact]
    public async Task A_save_over_a_newer_version_is_a_conflict_that_offers_reload_or_overwrite()
    {
        using var harness = new AuthHarness();
        var (context, cut, fake, _, request) = await SeededAsync(harness);
        using var _ = context;
        fake.ChangedElsewhere(request, Definition("https://api.example.test/theirs"));

        cut.Find("[data-testid='builder-url']").Input("https://api.example.test/mine");
        Save(cut);

        cut.WaitForState(() => cut.FindAll("[data-testid='builder-conflict']").Count == 1);
        Assert.Equal("alert", cut.Find("[data-testid='builder-conflict']").GetAttribute("role"));
        Assert.Equal("https://api.example.test/mine", UrlText(cut));

        cut.Find("[data-testid='builder-conflict-reload']").Click();

        cut.WaitForState(() => cut.FindAll("[data-testid='builder-conflict']").Count == 0);
        Assert.Equal("https://api.example.test/theirs", UrlText(cut));
        Assert.Single(cut.FindAll("[data-testid='builder-clean']"));
    }

    [Fact]
    public async Task Overwrite_after_a_conflict_writes_the_draft_over_the_newer_version()
    {
        using var harness = new AuthHarness();
        var (context, cut, fake, _, request) = await SeededAsync(harness);
        using var _ = context;
        fake.ChangedElsewhere(request, Definition("https://api.example.test/theirs"));

        cut.Find("[data-testid='builder-url']").Input("https://api.example.test/mine");
        Save(cut);
        cut.WaitForState(() => cut.FindAll("[data-testid='builder-conflict']").Count == 1);

        cut.Find("[data-testid='builder-conflict-overwrite']").Click();

        cut.WaitForState(() => cut.FindAll("[data-testid='builder-clean']").Count == 1);
        Assert.Equal("https://api.example.test/mine", fake.DefinitionOf(request)!.Url);
    }

    [Fact]
    public async Task A_rename_from_the_tree_does_not_make_the_next_save_a_conflict()
    {
        using var harness = new AuthHarness();
        var (context, cut, fake, _, request) = await SeededAsync(harness);
        using var _ = context;

        cut.Find("[data-testid='action-rename']").Click();
        cut.Find("[data-testid='rename-name']").Input("Renamed list");
        cut.Find("[data-testid='rename-submit']").Click();
        cut.WaitForState(() => cut.FindAll("[data-testid='rename-form']").Count == 0);

        cut.Find("[data-testid='builder-url']").Input("https://api.example.test/after-rename");
        Save(cut);

        cut.WaitForState(() => cut.FindAll("[data-testid='builder-clean']").Count == 1);
        Assert.Empty(cut.FindAll("[data-testid='builder-conflict']"));
        Assert.Equal("https://api.example.test/after-rename", fake.DefinitionOf(request)!.Url);
    }

    [Fact]
    public async Task Save_as_creates_a_new_request_in_the_chosen_collection_and_opens_it()
    {
        using var harness = new AuthHarness();
        var (context, cut, fake, collection, _) = await SeededAsync(harness);
        using var _ = context;

        cut.Find("[data-testid='builder-save-as']").Click();
        cut.Find("[data-testid='builder-saveas-name']").Input("Copy of list");
        cut.Find("[data-testid='builder-saveas']").Submit();

        cut.WaitForState(() => cut.FindAll("[data-testid='tabs-tab-2']").Count == 1);
        Assert.Equal("Copy of list", cut.Find("[data-testid='request-title']").TextContent);
        Assert.Contains($"POST /api/v1/collections/{collection}/requests", fake.Calls);
        Assert.Empty(cut.FindAll("[data-testid='builder-saveas']"));
    }

    [Fact]
    public async Task Save_as_refuses_a_blank_name_without_a_call()
    {
        using var harness = new AuthHarness();
        var (context, cut, fake, _, _) = await SeededAsync(harness);
        using var _ = context;
        var callsBefore = fake.Calls.Count;

        cut.Find("[data-testid='builder-save-as']").Click();
        cut.Find("[data-testid='builder-saveas-name']").Input("   ");
        cut.Find("[data-testid='builder-saveas']").Submit();

        cut.WaitForState(() => cut.FindAll("[data-testid='shell-notice']").Count == 1);
        Assert.DoesNotContain(fake.Calls.Skip(callsBefore), call => call.StartsWith("POST ", StringComparison.Ordinal));
    }

    [Fact]
    public async Task New_request_in_a_collection_opens_an_untitled_request_with_the_default_url()
    {
        using var harness = new AuthHarness();
        var principal = await MembershipPageTestHarness.SignedInAsync(harness, Guid.NewGuid(), WorkspaceRoles.Owner, Ct);
        var fake = new FakeCollectionsService(Guid.NewGuid());
        var collection = fake.AddCollection("Empty");
        using var context = CollectionsPageHarness.Create(harness, principal, fake);
        var cut = context.Render<WorkspaceHome>();
        cut.WaitForState(() => cut.FindAll($"[data-testid='tree-collection-{collection}']").Count == 1);
        cut.Find($"[data-testid='tree-collection-{collection}']").Click();
        cut.WaitForState(() => cut.FindAll("[data-testid='action-new-request']").Count == 1);

        cut.Find("[data-testid='action-new-request']").Click();

        cut.WaitForState(() => cut.FindAll("[data-testid='builder-url']").Count == 1);
        Assert.Equal("Untitled request", cut.Find("[data-testid='request-title']").TextContent);
        Assert.Equal(BuilderMessages.NewRequestUrl, UrlText(cut));
        Assert.Contains($"POST /api/v1/collections/{collection}/requests", fake.Calls);
    }

    [Fact]
    public async Task A_request_that_will_not_open_is_an_alert_with_a_retry_and_the_tab_stays()
    {
        using var harness = new AuthHarness();
        var principal = await MembershipPageTestHarness.SignedInAsync(harness, Guid.NewGuid(), WorkspaceRoles.Owner, Ct);
        var fake = new FakeCollectionsService(Guid.NewGuid());
        var collection = fake.AddCollection("Billing");
        var request = fake.AddRequest(collection, "Flaky");
        fake.FailingRequests.Add(request);
        using var context = CollectionsPageHarness.Create(harness, principal, fake);
        var cut = context.Render<WorkspaceHome>();
        cut.WaitForState(() => cut.FindAll($"[data-testid='tree-collection-{collection}']").Count == 1);
        cut.Find($"[data-testid='tree-toggle-{collection}']").Click();
        cut.WaitForState(() => cut.FindAll($"[data-testid='tree-request-{request}']").Count == 1);

        cut.Find($"[data-testid='tree-request-{request}']").Click();

        cut.WaitForState(() => cut.FindAll("[data-testid='request-load-failed']").Count == 1);
        Assert.Equal("alert", cut.Find("[data-testid='request-load-failed']").GetAttribute("role"));
        Assert.Single(cut.FindAll("[data-testid='tabs-tab-1']"));

        fake.FailingRequests.Clear();
        cut.Find("[data-testid='request-retry']").Click();

        cut.WaitForState(() => cut.FindAll("[data-testid='builder-url']").Count == 1);
        Assert.Empty(cut.FindAll("[data-testid='request-load-failed']"));
    }

    [Fact]
    public async Task A_json_body_is_beautified_and_an_invalid_one_says_where_it_is_wrong()
    {
        using var harness = new AuthHarness();
        var (context, cut, fake, _, request) = await SeededAsync(harness);
        using var _ = context;

        cut.Find("[data-testid='builder-tab-body']").Click();
        cut.Find("[data-testid='builder-body-type']").Change("raw");
        cut.WaitForState(() => cut.FindAll("[data-testid='builder-body-raw']").Count == 1);
        cut.Find("[data-testid='builder-body-raw']").Input("{\"a\":1,\"b\":{}}");
        cut.Find("[data-testid='builder-body-beautify']").Click();
        Save(cut);
        cut.WaitForState(() => cut.FindAll("[data-testid='builder-clean']").Count == 1);
        Assert.Contains("\n  \"a\": 1", fake.DefinitionOf(request)!.Body!.Raw);

        cut.Find("[data-testid='builder-body-raw']").Input("{\"a\": ,}");
        cut.Find("[data-testid='builder-body-beautify']").Click();

        cut.WaitForState(() => cut.FindAll("[data-testid='builder-body-beautify-error']").Count == 1);
        Assert.Matches(@"at line \d+, column \d+", cut.Find("[data-testid='builder-body-beautify-error']").TextContent);
    }

    [Fact]
    public async Task A_header_the_service_usually_sets_gets_a_note()
    {
        using var harness = new AuthHarness();
        var (context, cut, _, _, _) = await SeededAsync(harness);
        using var _ = context;

        cut.Find("[data-testid='builder-tab-headers']").Click();
        cut.Find("[data-testid='kv-add']").Click();
        cut.WaitForState(() => cut.FindAll("[data-testid='kv-key-0']").Count == 1);
        cut.Find("[data-testid='kv-key-0']").Input("Authorization");

        cut.WaitForState(() => cut.FindAll("[data-testid='kv-advice-0']").Count == 1);
    }

    [Fact]
    public async Task Bulk_edit_replaces_the_rows_from_key_value_lines()
    {
        using var harness = new AuthHarness();
        var (context, cut, _, _, _) = await SeededAsync(harness);
        using var _ = context;

        cut.Find("[data-testid='builder-tab-headers']").Click();
        cut.Find("[data-testid='kv-bulk-toggle']").Click();
        cut.Find("[data-testid='kv-bulk']").Input("Accept: application/json\n//X-Debug: 1");
        cut.Find("[data-testid='kv-bulk-apply']").Click();

        cut.WaitForState(() => cut.FindAll("[data-testid='kv-row-1']").Count == 1);
        Assert.Equal("Accept", cut.Find("[data-testid='kv-key-0']").GetAttribute("value"));
        Assert.Equal("X-Debug", cut.Find("[data-testid='kv-key-1']").GetAttribute("value"));
        Assert.Null(cut.Find("[data-testid='kv-enabled-1']").GetAttribute("checked"));
    }

    [Fact]
    public async Task A_form_body_field_that_is_empty_is_refused_by_the_page_before_a_call()
    {
        using var harness = new AuthHarness();
        var (context, cut, fake, _, _) = await SeededAsync(harness);
        using var _ = context;
        var callsBefore = fake.Calls.Count;

        cut.Find("[data-testid='builder-tab-body']").Click();
        cut.Find("[data-testid='builder-body-type']").Change("formUrlEncoded");
        cut.WaitForState(() => cut.FindAll("[data-testid='kv-add']").Count == 1);
        cut.Find("[data-testid='kv-add']").Click();
        cut.WaitForState(() => cut.FindAll("[data-testid='kv-key-0']").Count == 1);
        Save(cut);

        cut.WaitForState(() => cut.FindAll("[data-testid='kv-key-error-0']").Count == 1);
        Assert.Contains("1 to 256", cut.Find("[data-testid='kv-key-error-0']").TextContent);
        Assert.DoesNotContain(fake.Calls.Skip(callsBefore), call => call.StartsWith("PUT ", StringComparison.Ordinal));
    }
}
