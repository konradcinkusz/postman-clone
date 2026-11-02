using Microsoft.Playwright;
using PostmanClone.Web.E2E.Support;
using static Microsoft.Playwright.Assertions;

namespace PostmanClone.Web.E2E.Journeys;

/// <summary>
/// ADR-0030: a fresh start shows a populated product. The estate was started with the seed, so the two demo accounts exist in one
/// workspace, the demo collection holds its requests, and the demo environment holds a plain variable and a secret. These journeys
/// sign in through the form as the seeded accounts and read what the seed created through the product's own screens. They create
/// nothing, so they leave the demo data as they found it.
/// </summary>
[Trait(E2ESettings.EstateTrait, E2ESettings.ComposeEstate)]
public sealed class SeededJourneys : Journey
{
    [Fact]
    public async Task The_seeded_owner_signs_in_and_sees_the_demo_collection_with_its_requests()
    {
        var page = await NewPageAsync();
        await Ui.SignInAsync(page, DemoLogins.OwnerEmail, DemoLogins.Password);

        var tree = page.GetByTestId("tree");
        await Expect(tree).ToContainTextAsync(DemoLogins.Collection);
        await Workspace.ExpandAsync(page, DemoLogins.Collection);
        await Expect(tree).ToContainTextAsync(DemoLogins.GetRequest);
        await Workspace.ExpandAsync(page, DemoLogins.PostFolder);
        await Expect(tree).ToContainTextAsync(DemoLogins.PostRequest);
    }

    [Fact]
    public async Task The_seeded_member_signs_in_and_sees_the_same_demo_collection_in_the_shared_workspace()
    {
        var member = await NewPageAsync();
        await Ui.SignInAsync(member, DemoLogins.MemberEmail, DemoLogins.Password);
        await Expect(member.GetByTestId("tree")).ToContainTextAsync(DemoLogins.Collection);

        // The members page lists both accounts of the one workspace.
        await Ui.OpenAsync(member, "/workspace");
        var body = member.Locator("main");
        await Expect(body).ToContainTextAsync(DemoLogins.OwnerEmail);
        await Expect(body).ToContainTextAsync(DemoLogins.MemberEmail);
    }

    [Fact]
    public async Task The_demo_environment_has_a_plain_variable_and_a_secret_whose_value_is_never_shown()
    {
        var page = await NewPageAsync();
        await Ui.SignInAsync(page, DemoLogins.OwnerEmail, DemoLogins.Password);
        await Ui.OpenAsync(page, "/environments");
        await page.GetByTestId("env-list").GetByText(DemoLogins.Environment, new() { Exact = true }).ClickAsync();

        // The rows come back in the service's order, so find each variable by its key.
        await Expect(page.Locator("[data-testid^=env-var-key-]")).ToHaveCountAsync(2);
        var keys = new[] { await page.GetByTestId("env-var-key-0").InputValueAsync(), await page.GetByTestId("env-var-key-1").InputValueAsync() };
        Assert.Equal(["api_key", "base_url"], keys.Order(StringComparer.Ordinal).ToArray());
        var secretRow = Array.IndexOf(keys, "api_key");
        await Expect(page.GetByTestId($"env-var-state-{secretRow}")).ToContainTextAsync(new System.Text.RegularExpressions.Regex("stored", System.Text.RegularExpressions.RegexOptions.IgnoreCase));
        Assert.DoesNotContain(DemoLogins.FakeSecret, await page.ContentAsync());
    }

    [Fact]
    public async Task The_demo_GET_is_sent_through_the_server_sender_and_the_answer_is_shown()
    {
        var page = await NewPageAsync();
        await Ui.SignInAsync(page, DemoLogins.OwnerEmail, DemoLogins.Password);

        await Workspace.ExpandAsync(page, DemoLogins.Collection);
        await page.GetByTestId("tree").GetByText(DemoLogins.GetRequest, new() { Exact = true }).ClickAsync();
        await Expect(page.GetByTestId("builder-url")).ToHaveValueAsync("https://example.com/");

        // The sender is the server's by default (no agent is paired in this journey). example.com is a public host, so the
        // sender's SSRF guard lets it through in every configuration; the stack needs outbound internet access.
        await Workspace.SendAsync(page);
        await Expect(page.GetByTestId("response-status")).ToContainTextAsync("200");
        await Expect(page.GetByTestId("response-body")).ToContainTextAsync("Example Domain");
    }
}
