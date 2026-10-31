using System.Net;
using Bunit;
using PostmanClone.ServiceDefaults.Authentication;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.Extensions.DependencyInjection;
using PostmanClone.Web.Api;
using PostmanClone.Web.Collections;
using PostmanClone.Web.Components.Pages;
using PostmanClone.Web.Tests.Auth;
using PostmanClone.Web.Tests.Membership;

namespace PostmanClone.Web.Tests.Collections;

/// <summary>
/// The import page (WP23 acceptance, bUnit): the file and the command are checked before anything is sent, the upload goes to the
/// service, the result is shown with its warnings grouped by code, and each refusal is said in words. The page runs with the real
/// shell and client; only the service and authservice are fakes.
/// </summary>
public sealed class ImportPageTests
{
    private const string BillingJson = "{\"info\":{\"name\":\"Billing\"},\"item\":[]}";

    private static CancellationToken Ct => Xunit.TestContext.Current.CancellationToken;

    /// <summary>The tab is a query parameter, so the test navigates to it as the browser would, then renders.</summary>
    private static IRenderedComponent<Import> RenderImport(BunitContext context, string? tab = null)
    {
        if (tab is not null)
        {
            context.Services.GetRequiredService<NavigationManager>().NavigateTo($"/import?tab={tab}");
        }

        return context.Render<Import>();
    }

    [Fact]
    public async Task The_postman_tab_offers_a_json_file_and_says_its_limit()
    {
        using var harness = new AuthHarness();
        var principal = await MembershipPageTestHarness.SignedInAsync(harness, Guid.NewGuid(), WorkspaceRoles.Member, Ct);
        var fake = new FakeCollectionsService(Guid.NewGuid());
        using var context = CollectionsPageHarness.Create(harness, principal, fake);

        var cut = context.Render<Import>();

        cut.WaitForState(() => cut.FindAll("[data-testid='import-file-form']").Count == 1);
        var input = cut.Find("input[type=file]");
        Assert.Contains(".json", input.GetAttribute("accept"));
        Assert.Contains("5 MB", cut.Find("[data-testid='import-file-hint']").TextContent);
        Assert.True(cut.Find("[data-testid='import-file-submit']").HasAttribute("disabled"));
    }

    [Fact]
    public async Task A_file_over_the_limit_is_refused_when_it_is_chosen_and_nothing_is_sent()
    {
        using var harness = new AuthHarness();
        var principal = await MembershipPageTestHarness.SignedInAsync(harness, Guid.NewGuid(), WorkspaceRoles.Member, Ct);
        var fake = new FakeCollectionsService(Guid.NewGuid());
        using var context = CollectionsPageHarness.Create(harness, principal, fake);
        var cut = context.Render<Import>();
        cut.WaitForState(() => cut.FindAll("[data-testid='import-file-form']").Count == 1);

        cut.FindComponent<InputFile>().UploadFiles(
            InputFileContent.CreateFromBinary(new byte[ImportRules.MaximumFileBytes + 1], "big.json", contentType: "application/json"));

        cut.WaitForState(() => cut.FindAll("[data-testid='import-error']").Count == 1);
        Assert.Contains("5 MB", cut.Find("[data-testid='import-error']").TextContent);
        Assert.Equal("alert", cut.Find("[data-testid='import-error']").GetAttribute("role"));
        Assert.True(cut.Find("[data-testid='import-file-submit']").HasAttribute("disabled"));
        Assert.DoesNotContain(fake.Calls, call => call.StartsWith("POST /api/v1/import", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_file_that_is_not_json_is_refused_when_it_is_chosen()
    {
        using var harness = new AuthHarness();
        var principal = await MembershipPageTestHarness.SignedInAsync(harness, Guid.NewGuid(), WorkspaceRoles.Member, Ct);
        var fake = new FakeCollectionsService(Guid.NewGuid());
        using var context = CollectionsPageHarness.Create(harness, principal, fake);
        var cut = context.Render<Import>();
        cut.WaitForState(() => cut.FindAll("[data-testid='import-file-form']").Count == 1);

        cut.FindComponent<InputFile>().UploadFiles(InputFileContent.CreateFromText("hello", "notes.txt", contentType: "text/plain"));

        cut.WaitForState(() => cut.FindAll("[data-testid='import-error']").Count == 1);
        Assert.Contains(".json", cut.Find("[data-testid='import-error']").TextContent);
    }

    [Fact]
    public async Task A_postman_file_is_streamed_to_the_service_and_its_warnings_are_grouped_by_code()
    {
        using var harness = new AuthHarness();
        var principal = await MembershipPageTestHarness.SignedInAsync(harness, Guid.NewGuid(), WorkspaceRoles.Member, Ct);
        var fake = new FakeCollectionsService(Guid.NewGuid())
        {
            NextImportName = "Billing",
        };
        fake.NextWarnings.AddRange(
        [
            new ImportWarning("unsupported_body", "Body of \"Upload\" was not imported.", "$.item[2].request.body"),
            new ImportWarning("secret_not_imported", "Replaced \"Login\" bearer with imported_auth_1.", "$.item[0].request.auth"),
            new ImportWarning("unsupported_body", "Body of \"Export\" was not imported.", "$.item[5].request.body"),
            new ImportWarning("scripts_ignored", "Scripts of \"Health\" were not run.", "$.item[1].event"),
        ]);
        using var context = CollectionsPageHarness.Create(harness, principal, fake);
        var cut = context.Render<Import>();
        cut.WaitForState(() => cut.FindAll("[data-testid='import-file-form']").Count == 1);

        cut.FindComponent<InputFile>().UploadFiles(InputFileContent.CreateFromText(BillingJson, "billing.json", contentType: "application/json"));
        cut.WaitForState(() => cut.Find("[data-testid='import-file-submit']").HasAttribute("disabled") is false);
        cut.Find("[data-testid='import-file-form']").Submit();

        cut.WaitForState(() => cut.FindAll("[data-testid='import-result']").Count == 1);
        Assert.Equal("multipart/form-data", fake.LastImportMediaType);
        Assert.Contains(BillingJson, fake.LastImportContent, StringComparison.Ordinal);
        Assert.Contains("Created \"Billing\"", cut.Find("[data-testid='import-summary']").TextContent);

        // One group per code, the secret group first, the repeated code counted once with both of its items.
        var groups = cut.FindAll("[data-testid^='warnings-group-']").Select(group => group.GetAttribute("data-testid")).ToArray();
        Assert.Equal(["warnings-group-secret-not-imported", "warnings-group-unsupported-body", "warnings-group-scripts-ignored"], groups);
        Assert.Equal("2", cut.Find("[data-testid='warnings-count-unsupported-body']").TextContent.Trim());
        Assert.Equal(4, cut.FindAll("[data-testid='warning-item']").Count);
        Assert.Contains("$.item[5].request.body", cut.Find("[data-testid='warnings-group-unsupported-body']").TextContent);
    }

    [Fact]
    public async Task The_secret_group_links_to_the_environments_page_where_the_variables_are_made()
    {
        using var harness = new AuthHarness();
        var principal = await MembershipPageTestHarness.SignedInAsync(harness, Guid.NewGuid(), WorkspaceRoles.Member, Ct);
        var fake = new FakeCollectionsService(Guid.NewGuid());
        var collection = fake.AddCollection("Billing");
        fake.NextWarnings.Add(new ImportWarning("secret_not_imported", "Replaced a bearer with imported_auth_1.", "$.item[0]"));
        using var context = CollectionsPageHarness.Create(harness, principal, fake);
        var cut = RenderImport(context, "curl");
        cut.WaitForState(() => cut.FindAll($"#import-curl-collection option[value='{collection}']").Count == 1);
        cut.Find("#import-curl-collection").Change(collection.ToString());
        cut.Find("[data-testid='import-curl-command']").Input("curl https://api.example.test/items");

        cut.Find("[data-testid='import-curl-form']").Submit();

        cut.WaitForState(() => cut.FindAll("[data-testid='import-result']").Count == 1);
        var link = cut.Find("[data-testid='import-create-variables']");
        Assert.Equal("/environments", link.GetAttribute("href"));
    }

    [Fact]
    public async Task A_refused_file_import_says_why_in_words_and_is_an_alert()
    {
        using var harness = new AuthHarness();
        var principal = await MembershipPageTestHarness.SignedInAsync(harness, Guid.NewGuid(), WorkspaceRoles.Member, Ct);
        var fake = new FakeCollectionsService(Guid.NewGuid()) { ImportRefusal = (HttpStatusCode.BadRequest, "import_invalid") };
        using var context = CollectionsPageHarness.Create(harness, principal, fake);
        var cut = context.Render<Import>();
        cut.WaitForState(() => cut.FindAll("[data-testid='import-file-form']").Count == 1);
        cut.FindComponent<InputFile>().UploadFiles(InputFileContent.CreateFromText(BillingJson, "billing.json", contentType: "application/json"));
        cut.WaitForState(() => cut.Find("[data-testid='import-file-submit']").HasAttribute("disabled") is false);

        cut.Find("[data-testid='import-file-form']").Submit();

        cut.WaitForState(() => cut.FindAll("[data-testid='import-error']").Count == 1);
        Assert.Contains("not a valid Postman v2.1 collection", cut.Find("[data-testid='import-error']").TextContent);
        Assert.Empty(cut.FindAll("[data-testid='import-result']"));
    }

    [Fact]
    public async Task A_file_the_service_calls_too_large_names_the_limit()
    {
        using var harness = new AuthHarness();
        var principal = await MembershipPageTestHarness.SignedInAsync(harness, Guid.NewGuid(), WorkspaceRoles.Member, Ct);
        var fake = new FakeCollectionsService(Guid.NewGuid()) { ImportRefusal = (HttpStatusCode.RequestEntityTooLarge, "import_too_large") };
        using var context = CollectionsPageHarness.Create(harness, principal, fake);
        var cut = context.Render<Import>();
        cut.WaitForState(() => cut.FindAll("[data-testid='import-file-form']").Count == 1);
        cut.FindComponent<InputFile>().UploadFiles(InputFileContent.CreateFromText(BillingJson, "billing.json", contentType: "application/json"));
        cut.WaitForState(() => cut.Find("[data-testid='import-file-submit']").HasAttribute("disabled") is false);

        cut.Find("[data-testid='import-file-form']").Submit();

        cut.WaitForState(() => cut.FindAll("[data-testid='import-error']").Count == 1);
        Assert.Contains("5 MB", cut.Find("[data-testid='import-error']").TextContent);
    }

    [Fact]
    public async Task A_blank_command_is_refused_before_the_service_is_called()
    {
        using var harness = new AuthHarness();
        var principal = await MembershipPageTestHarness.SignedInAsync(harness, Guid.NewGuid(), WorkspaceRoles.Member, Ct);
        var fake = new FakeCollectionsService(Guid.NewGuid());
        using var context = CollectionsPageHarness.Create(harness, principal, fake);
        var cut = RenderImport(context, "curl");
        cut.WaitForState(() => cut.FindAll("[data-testid='import-curl-form']").Count == 1);

        cut.Find("[data-testid='import-curl-form']").Submit();

        cut.WaitForState(() => cut.FindAll("[data-testid='import-error']").Count == 1);
        Assert.Contains("Paste a cURL command", cut.Find("[data-testid='import-error']").TextContent);
        Assert.DoesNotContain(fake.Calls, call => call.StartsWith("POST /api/v1/import", StringComparison.Ordinal));
    }

    [Fact]
    public async Task The_command_counter_shows_the_length_and_an_over_long_command_is_refused()
    {
        using var harness = new AuthHarness();
        var principal = await MembershipPageTestHarness.SignedInAsync(harness, Guid.NewGuid(), WorkspaceRoles.Member, Ct);
        var fake = new FakeCollectionsService(Guid.NewGuid());
        using var context = CollectionsPageHarness.Create(harness, principal, fake);
        var collection = fake.AddCollection("Billing");
        var cut = RenderImport(context, "curl");
        cut.WaitForState(() => cut.FindAll("[data-testid='import-curl-form']").Count == 1);
        cut.WaitForState(() => cut.FindAll($"#import-curl-collection option[value='{collection}']").Count == 1);
        cut.Find("#import-curl-collection").Change(collection.ToString());

        cut.Find("[data-testid='import-curl-command']").Input(new string('a', ImportRules.MaximumCommandLength + 1));

        Assert.Equal($"{ImportRules.MaximumCommandLength + 1} / {ImportRules.MaximumCommandLength} characters", cut.Find("[data-testid='import-curl-counter']").TextContent);
        cut.Find("[data-testid='import-curl-form']").Submit();
        cut.WaitForState(() => cut.FindAll("[data-testid='import-error']").Count == 1);
        Assert.Contains("longer than 20000", cut.Find("[data-testid='import-error']").TextContent);
        Assert.DoesNotContain(fake.Calls, call => call.StartsWith("POST /api/v1/import", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_curl_command_is_imported_into_the_chosen_folder_and_the_warnings_are_shown()
    {
        using var harness = new AuthHarness();
        var principal = await MembershipPageTestHarness.SignedInAsync(harness, Guid.NewGuid(), WorkspaceRoles.Member, Ct);
        var fake = new FakeCollectionsService(Guid.NewGuid());
        var collection = fake.AddCollection("Billing");
        var folder = fake.AddFolder(collection, "Invoices");
        fake.NextWarnings.Add(new ImportWarning("unsupported_option", "The option --compressed is not supported.", "command[3]"));
        const string command = "curl --compressed 'https://api.example.test/invoices'";
        using var context = CollectionsPageHarness.Create(harness, principal, fake);
        var cut = RenderImport(context, "curl");
        cut.WaitForState(() => cut.FindAll($"#import-curl-collection option[value='{collection}']").Count == 1);

        cut.Find("#import-curl-collection").Change(collection.ToString());
        cut.WaitForState(() => cut.FindAll($"#import-curl-folder option[value='{folder}']").Count == 1);
        cut.Find("#import-curl-folder").Change(folder.ToString());
        cut.Find("[data-testid='import-curl-command']").Input(command);

        cut.Find("[data-testid='import-curl-form']").Submit();

        cut.WaitForState(() => cut.FindAll("[data-testid='import-result']").Count == 1);
        Assert.Equal(command, fake.LastCurlCommand);
        Assert.Equal(folder, fake.LastCurlFolder);
        Assert.Equal("Added 1 request to \"Billing\".", cut.Find("[data-testid='import-summary']").TextContent);
        Assert.Contains("--compressed", cut.Find("[data-testid='warnings-group-unsupported-option']").TextContent);
        Assert.Equal("1", cut.Find("[data-testid='warnings-count-unsupported-option']").TextContent.Trim());
    }

    [Fact]
    public async Task A_warning_message_from_the_file_is_shown_as_text_and_never_as_markup()
    {
        using var harness = new AuthHarness();
        var principal = await MembershipPageTestHarness.SignedInAsync(harness, Guid.NewGuid(), WorkspaceRoles.Member, Ct);
        var fake = new FakeCollectionsService(Guid.NewGuid());
        fake.NextWarnings.Add(new ImportWarning("unsupported_body", "Body of \"<img src=x onerror=alert(1)>\" was not imported.", "$.item[0]"));
        using var context = CollectionsPageHarness.Create(harness, principal, fake);
        var cut = context.Render<Import>();
        cut.WaitForState(() => cut.FindAll("[data-testid='import-file-form']").Count == 1);
        cut.FindComponent<InputFile>().UploadFiles(InputFileContent.CreateFromText(BillingJson, "billing.json", contentType: "application/json"));
        cut.WaitForState(() => cut.Find("[data-testid='import-file-submit']").HasAttribute("disabled") is false);

        cut.Find("[data-testid='import-file-form']").Submit();

        cut.WaitForState(() => cut.FindAll("[data-testid='warning-item']").Count == 1);
        Assert.Empty(cut.FindAll("img"));
        Assert.Contains("&lt;img", cut.Find("[data-testid='warning-item']").InnerHtml);
    }
}
