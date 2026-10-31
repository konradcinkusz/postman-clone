using System.Text.Json.Nodes;
using Bunit;
using PostmanClone.ServiceDefaults.Authentication;
using PostmanClone.Web.Api;
using PostmanClone.Web.Components.Environments;
using PostmanClone.Web.Environments;
using PostmanClone.Web.Tests.Auth;
using PostmanClone.Web.Tests.Membership;

namespace PostmanClone.Web.Tests.EnvironmentsUi;

/// <summary>
/// The environments page and the top-bar selector rendered by bUnit (WP21 acceptance). The central claim is the write-only rule:
/// a secret's value never reaches the markup, at any state the page can be in. Each secret value below is a sentinel the test
/// searches the rendered HTML for. None of them is a credential.
/// </summary>
public sealed class EnvironmentsPageTests
{
    private const string StoredSentinel = "page-stored-sentinel-4417"; // gitleaks:allow

    private const string TypedSentinel = "page-typed-sentinel-8802"; // gitleaks:allow

    private static CancellationToken Ct => Xunit.TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_secret_value_is_never_in_the_markup_after_load_after_typing_and_after_a_failed_save()
    {
        using var harness = new AuthHarness();
        var principal = await MembershipPageTestHarness.SignedInAsync(harness, Guid.NewGuid(), WorkspaceRoles.Owner, Ct);
        var fake = new FakeEnvironmentsService { SecretsUnavailable = true };
        fake.AddEnvironment(
            "Staging",
            new FakeEnvironmentsService.FakeVariable("baseUrl", "https://staging.example"),
            new FakeEnvironmentsService.FakeVariable("token", StoredSentinel, IsSecret: true));
        using var rig = EnvironmentsRig.Create(harness, principal, fake);

        var cut = rig.Context.Render<global::PostmanClone.Web.Components.Pages.Environments>();
        cut.WaitForState(() => cut.FindAll("[data-testid='env-item-1']").Count == 1);
        Assert.DoesNotContain(StoredSentinel, cut.Markup);

        cut.Find("[data-testid='env-item-1']").Click();
        cut.WaitForState(() => cut.FindAll("[data-testid='env-editor']").Count == 1);
        Assert.DoesNotContain(StoredSentinel, cut.Markup);
        Assert.Equal("stored", cut.Find("[data-testid='env-var-state-1']").TextContent.Trim());

        cut.Find("[data-testid='env-var-secret-value-1']").Input(TypedSentinel);
        Assert.DoesNotContain(TypedSentinel, cut.Markup);
        Assert.DoesNotContain(StoredSentinel, cut.Markup);

        cut.Find("[data-testid='env-save']").Click();
        cut.WaitForState(() => cut.FindAll("[data-testid='env-secrets-unavailable']").Count == 1);
        Assert.DoesNotContain(TypedSentinel, cut.Markup);
        Assert.DoesNotContain(StoredSentinel, cut.Markup);
        Assert.Equal("alert", cut.Find("[data-testid='env-secrets-unavailable']").GetAttribute("role"));
        Assert.DoesNotContain(fake.Bodies, body => body.Contains(StoredSentinel, StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_save_that_does_not_retype_a_secret_keeps_it_and_sends_no_value_for_it()
    {
        using var harness = new AuthHarness();
        var principal = await MembershipPageTestHarness.SignedInAsync(harness, Guid.NewGuid(), WorkspaceRoles.Owner, Ct);
        var fake = new FakeEnvironmentsService();
        var id = fake.AddEnvironment(
            "Staging",
            new FakeEnvironmentsService.FakeVariable("baseUrl", "https://staging.example"),
            new FakeEnvironmentsService.FakeVariable("token", StoredSentinel, IsSecret: true));
        using var rig = EnvironmentsRig.Create(harness, principal, fake);
        var cut = OpenEnvironment(rig, id);

        cut.Find("[data-testid='env-var-value-0']").Input("https://staging.example/v2");
        cut.Find("[data-testid='env-save']").Click();
        cut.WaitForState(() => cut.FindAll("[data-testid='env-notice']").Count == 1);

        var body = JsonNode.Parse(Assert.Single(fake.Bodies))!["variables"]!.AsArray();
        var token = body.Single(variable => variable!["key"]!.GetValue<string>() == "token")!;
        Assert.True(token["keepExistingSecret"]!.GetValue<bool>());
        Assert.Null(token["value"]);
        Assert.True(fake.StoredHasSecret(id, "token"));
        Assert.Equal("https://staging.example/v2", fake.StoredPlainValue(id, "baseUrl"));
        Assert.DoesNotContain(StoredSentinel, cut.Markup);
    }

    [Fact]
    public async Task A_typed_secret_is_sent_as_its_new_value_and_the_page_forgets_it_after_the_save()
    {
        using var harness = new AuthHarness();
        var principal = await MembershipPageTestHarness.SignedInAsync(harness, Guid.NewGuid(), WorkspaceRoles.Owner, Ct);
        var fake = new FakeEnvironmentsService();
        var id = fake.AddEnvironment("Staging", new FakeEnvironmentsService.FakeVariable("token", StoredSentinel, IsSecret: true));
        using var rig = EnvironmentsRig.Create(harness, principal, fake);
        var cut = OpenEnvironment(rig, id);

        cut.Find("[data-testid='env-var-secret-value-0']").Input(TypedSentinel);
        cut.Find("[data-testid='env-save']").Click();
        cut.WaitForState(() => cut.FindAll("[data-testid='env-notice']").Count == 1);

        var token = JsonNode.Parse(Assert.Single(fake.Bodies))!["variables"]![0]!;
        Assert.Equal(TypedSentinel, token["value"]!.GetValue<string>());
        Assert.False(token["keepExistingSecret"]!.GetValue<bool>());
        Assert.DoesNotContain(TypedSentinel, cut.Markup);
        Assert.Equal("stored", cut.Find("[data-testid='env-var-state-0']").TextContent.Trim());
        Assert.Equal("Saved.", cut.Find("[data-testid='env-notice']").TextContent.Trim());
    }

    [Fact]
    public async Task A_stored_secret_made_plain_asks_for_a_value_and_sends_nothing()
    {
        using var harness = new AuthHarness();
        var principal = await MembershipPageTestHarness.SignedInAsync(harness, Guid.NewGuid(), WorkspaceRoles.Owner, Ct);
        var fake = new FakeEnvironmentsService();
        var id = fake.AddEnvironment("Staging", new FakeEnvironmentsService.FakeVariable("token", StoredSentinel, IsSecret: true));
        using var rig = EnvironmentsRig.Create(harness, principal, fake);
        var cut = OpenEnvironment(rig, id);

        cut.Find("[data-testid='env-var-secret-0']").Change(false);

        Assert.Contains(EnvironmentMessages.PlainNeedsValue, cut.Find("[data-testid='env-var-error-0']").TextContent);
        Assert.True(cut.Find("[data-testid='env-save']").HasAttribute("disabled"));
        Assert.DoesNotContain(StoredSentinel, cut.Markup);
        Assert.Empty(fake.Bodies);
    }

    [Fact]
    public async Task A_new_secret_without_a_value_is_refused_until_one_is_typed()
    {
        using var harness = new AuthHarness();
        var principal = await MembershipPageTestHarness.SignedInAsync(harness, Guid.NewGuid(), WorkspaceRoles.Owner, Ct);
        var fake = new FakeEnvironmentsService();
        var id = fake.AddEnvironment("Staging", new FakeEnvironmentsService.FakeVariable("baseUrl", "https://staging.example"));
        using var rig = EnvironmentsRig.Create(harness, principal, fake);
        var cut = OpenEnvironment(rig, id);

        cut.Find("[data-testid='env-var-add']").Click();
        cut.Find("[data-testid='env-var-key-1']").Input("apiKey");
        cut.Find("[data-testid='env-var-secret-1']").Change(true);

        Assert.Contains(EnvironmentMessages.SecretNeedsValue, cut.Find("[data-testid='env-var-error-1']").TextContent);
        Assert.True(cut.Find("[data-testid='env-save']").HasAttribute("disabled"));

        cut.Find("[data-testid='env-var-secret-value-1']").Input(TypedSentinel);

        Assert.Empty(cut.FindAll("[data-testid='env-var-error-1']"));
        Assert.False(cut.Find("[data-testid='env-save']").HasAttribute("disabled"));
    }

    [Fact]
    public async Task Duplicate_keys_mark_both_rows_and_block_the_save()
    {
        using var harness = new AuthHarness();
        var principal = await MembershipPageTestHarness.SignedInAsync(harness, Guid.NewGuid(), WorkspaceRoles.Owner, Ct);
        var fake = new FakeEnvironmentsService();
        var id = fake.AddEnvironment("Staging", new FakeEnvironmentsService.FakeVariable("a", "1"), new FakeEnvironmentsService.FakeVariable("b", "2"));
        using var rig = EnvironmentsRig.Create(harness, principal, fake);
        var cut = OpenEnvironment(rig, id);

        cut.Find("[data-testid='env-var-add']").Click();
        cut.Find("[data-testid='env-var-key-2']").Input("a");

        Assert.Contains(EnvironmentMessages.KeyDuplicate, cut.Find("[data-testid='env-var-error-0']").TextContent);
        Assert.Contains(EnvironmentMessages.KeyDuplicate, cut.Find("[data-testid='env-var-error-2']").TextContent);
        Assert.Empty(cut.FindAll("[data-testid='env-var-error-1']"));
        Assert.True(cut.Find("[data-testid='env-save']").HasAttribute("disabled"));
    }

    [Fact]
    public async Task At_the_limit_create_is_disabled_and_says_why()
    {
        using var harness = new AuthHarness();
        var principal = await MembershipPageTestHarness.SignedInAsync(harness, Guid.NewGuid(), WorkspaceRoles.Owner, Ct);
        var fake = new FakeEnvironmentsService();
        for (var index = 0; index < EnvironmentRules.EnvironmentsPerWorkspace; index++)
        {
            fake.AddEnvironment($"Env {index:D2}");
        }

        using var rig = EnvironmentsRig.Create(harness, principal, fake);
        var cut = rig.Context.Render<global::PostmanClone.Web.Components.Pages.Environments>();
        cut.WaitForState(() => cut.FindAll("[data-testid='env-limit']").Count == 1);

        Assert.True(cut.Find("[data-testid='env-create']").HasAttribute("disabled"));
        Assert.True(cut.Find("[data-testid='env-new-name']").HasAttribute("disabled"));
        Assert.Equal(EnvironmentMessages.LimitReached, cut.Find("[data-testid='env-limit']").TextContent.Trim());
    }

    [Fact]
    public async Task Creating_an_environment_opens_it_and_lists_it()
    {
        using var harness = new AuthHarness();
        var principal = await MembershipPageTestHarness.SignedInAsync(harness, Guid.NewGuid(), WorkspaceRoles.Owner, Ct);
        var fake = new FakeEnvironmentsService();
        using var rig = EnvironmentsRig.Create(harness, principal, fake);
        var cut = rig.Context.Render<global::PostmanClone.Web.Components.Pages.Environments>();
        cut.WaitForState(() => cut.FindAll("[data-testid='env-empty']").Count == 1);

        cut.Find("[data-testid='env-new-name']").Input("Production");
        cut.Find("[data-testid='env-create']").Click();
        cut.WaitForState(() => cut.FindAll("[data-testid='env-editor']").Count == 1);

        Assert.Equal("Production", cut.Find("[data-testid='env-name']").GetAttribute("value"));
        Assert.Equal(1, fake.Count);
        Assert.Contains("Production", cut.Find("[data-testid='env-list']").TextContent);
        Assert.Equal("Environment created.", cut.Find("[data-testid='env-notice']").TextContent.Trim());
    }

    [Fact]
    public async Task An_environment_that_is_gone_says_so_and_the_list_is_read_again()
    {
        using var harness = new AuthHarness();
        var principal = await MembershipPageTestHarness.SignedInAsync(harness, Guid.NewGuid(), WorkspaceRoles.Owner, Ct);
        var fake = new FakeEnvironmentsService();
        var gone = fake.AddEnvironment("Gone soon");
        fake.AddEnvironment("Still here");
        using var rig = EnvironmentsRig.Create(harness, principal, fake);
        var cut = OpenEnvironment(rig, gone);

        cut.Find("[data-testid='env-name']").Input("Edited before the other user deleted it");
        fake.RemoveEnvironment(gone);
        cut.Find("[data-testid='env-discard']").Click();
        cut.WaitForState(() => cut.FindAll("[data-testid='env-none-selected']").Count == 1);

        Assert.Equal(EnvironmentMessages.NotFound, cut.Find("[data-testid='env-error']").TextContent.Trim());
        Assert.Single(cut.FindAll("[data-testid='env-item-1']"));
        Assert.DoesNotContain("Gone soon", cut.Markup);
    }

    [Fact]
    public async Task Delete_asks_twice_and_a_refused_delete_keeps_the_environment()
    {
        using var harness = new AuthHarness();
        var principal = await MembershipPageTestHarness.SignedInAsync(harness, Guid.NewGuid(), WorkspaceRoles.Member, Ct);
        var fake = new FakeEnvironmentsService { ForbidDelete = true };
        var id = fake.AddEnvironment("Staging");
        using var rig = EnvironmentsRig.Create(harness, principal, fake);
        var cut = OpenEnvironment(rig, id);

        cut.Find("[data-testid='env-delete']").Click();
        Assert.Single(cut.FindAll("[data-testid='env-delete-confirm-text']"));
        cut.Find("[data-testid='env-delete-cancel']").Click();
        Assert.Empty(cut.FindAll("[data-testid='env-delete-confirm-text']"));

        cut.Find("[data-testid='env-delete']").Click();
        cut.Find("[data-testid='env-delete-confirm']").Click();
        cut.WaitForState(() => cut.FindAll("[data-testid='env-error']").Count == 1);

        Assert.Equal(EnvironmentMessages.DeleteForbidden, cut.Find("[data-testid='env-error']").TextContent.Trim());
        Assert.Single(cut.FindAll("[data-testid='env-editor']"));
        Assert.Equal(1, fake.Count);
    }

    [Fact]
    public async Task Delete_removes_the_environment_after_the_second_step()
    {
        using var harness = new AuthHarness();
        var principal = await MembershipPageTestHarness.SignedInAsync(harness, Guid.NewGuid(), WorkspaceRoles.Owner, Ct);
        var fake = new FakeEnvironmentsService();
        var id = fake.AddEnvironment("Staging");
        using var rig = EnvironmentsRig.Create(harness, principal, fake);
        var cut = OpenEnvironment(rig, id);

        cut.Find("[data-testid='env-delete']").Click();
        cut.Find("[data-testid='env-delete-confirm']").Click();
        cut.WaitForState(() => cut.FindAll("[data-testid='env-none-selected']").Count == 1);

        Assert.Equal(0, fake.Count);
        Assert.Equal("Environment deleted.", cut.Find("[data-testid='env-notice']").TextContent.Trim());
        Assert.Contains("No environments yet", cut.Markup);
    }

    [Fact]
    public async Task A_failed_list_is_an_alert_and_the_form_stays()
    {
        using var harness = new AuthHarness();
        var principal = await MembershipPageTestHarness.SignedInAsync(harness, Guid.NewGuid(), WorkspaceRoles.Owner, Ct);
        var fake = new FakeEnvironmentsService { Unavailable = true };
        using var rig = EnvironmentsRig.Create(harness, principal, fake);

        var cut = rig.Context.Render<global::PostmanClone.Web.Components.Pages.Environments>();
        cut.WaitForState(() => cut.FindAll("[data-testid='env-alert']").Count == 1);

        Assert.Equal(EnvironmentMessages.ForFailure(CollectionOutcome.Unavailable, null), cut.Find("[data-testid='env-alert']").TextContent.Trim());
        Assert.Equal("alert", cut.Find("[data-testid='env-alert']").GetAttribute("role"));
        Assert.Single(cut.FindAll("[data-testid='env-create-form']"));
    }

    [Fact]
    public async Task The_top_bar_choice_is_held_for_the_sends_with_its_enabled_variable_names()
    {
        using var harness = new AuthHarness();
        var principal = await MembershipPageTestHarness.SignedInAsync(harness, Guid.NewGuid(), WorkspaceRoles.Owner, Ct);
        var fake = new FakeEnvironmentsService();
        var id = fake.AddEnvironment(
            "Staging",
            new FakeEnvironmentsService.FakeVariable("baseUrl", "https://staging.example"),
            new FakeEnvironmentsService.FakeVariable("token", StoredSentinel, IsSecret: true),
            new FakeEnvironmentsService.FakeVariable("off", "x", Enabled: false));
        using var rig = EnvironmentsRig.Create(harness, principal, fake);

        var cut = rig.Context.Render<EnvironmentSelector>();
        cut.WaitForState(() => !cut.Find("[data-testid='environment-selector']").HasAttribute("disabled"));
        Assert.Equal(2, cut.FindAll("[data-testid='environment-selector'] option").Count);

        cut.Find("[data-testid='environment-selector']").Change(id.ToString());
        cut.WaitForState(() => rig.Selection.EnvironmentId == id);

        Assert.Equal("Staging", rig.Selection.Name);
        Assert.Equal(new[] { "baseUrl", "token" }, rig.Selection.KnownKeys!.Order());
        Assert.DoesNotContain(StoredSentinel, cut.Markup);

        cut.Find("[data-testid='environment-selector']").Change(string.Empty);
        cut.WaitForState(() => rig.Selection.EnvironmentId is null);
        Assert.Null(rig.Selection.KnownKeys);
    }

    [Fact]
    public async Task The_list_is_read_once_on_the_first_render_and_not_again_by_the_markup()
    {
        using var harness = new AuthHarness();
        var principal = await MembershipPageTestHarness.SignedInAsync(harness, Guid.NewGuid(), WorkspaceRoles.Owner, Ct);
        var fake = new FakeEnvironmentsService();
        fake.AddEnvironment("Staging");
        using var rig = EnvironmentsRig.Create(harness, principal, fake);

        var cut = rig.Context.Render<global::PostmanClone.Web.Components.Pages.Environments>();
        cut.WaitForState(() => cut.FindAll("[data-testid='env-item-1']").Count == 1);

        // One list read per render of the page: the first render reads it once, not once per call of the markup.
        Assert.Equal(1, fake.Calls.Count(call => call == "GET /api/v1/environments"));
    }

    private static Bunit.IRenderedComponent<global::PostmanClone.Web.Components.Pages.Environments> OpenEnvironment(EnvironmentsRig rig, Guid id)
    {
        var cut = rig.Context.Render<global::PostmanClone.Web.Components.Pages.Environments>();
        cut.WaitForState(() => cut.FindAll($"[data-testid='env-item-1']").Count == 1);
        cut.Find("[data-testid='env-item-1']").Click();
        cut.WaitForState(() => cut.FindAll("[data-testid='env-editor']").Count == 1);
        return cut;
    }
}
