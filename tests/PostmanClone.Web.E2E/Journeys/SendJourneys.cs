using System.Text.RegularExpressions;
using Microsoft.Playwright;
using PostmanClone.Web.E2E.Support;
using static Microsoft.Playwright.Assertions;

namespace PostmanClone.Web.E2E.Journeys;

/// <summary>
/// J4 to J7 (WP26): a request is sent from the builder to a target on this machine, and the answer is shown. The target
/// needs the loopback flag (E2E_SENDER_LOOPBACK=1), so these run in the loopback job. The strict journey runs in the
/// strict job, where the same send must be refused with its reason. The metadata address is refused in both jobs.
/// </summary>
public sealed class SendJourneys : Journey
{
    [Fact]
    [Trait(E2ESettings.ModeTrait, E2ESettings.LoopbackMode)]
    public async Task Get_to_the_local_target_shows_the_status_and_the_body()
    {
        await using var target = new TargetServer();
        await target.StartAsync();
        var user = await TestUsers.CreateAsync();
        var page = await NewPageAsync();
        await Ui.SignInAsync(page, user.Email, user.Password);

        await Workspace.NewRequestInNewCollectionAsync(page);
        await Workspace.SetUrlAsync(page, new Uri(target.BaseAddress, "json").ToString());
        await Workspace.SendAsync(page);

        await Expect(page.GetByTestId("response-status")).ToContainTextAsync("200");
        await Expect(page.GetByTestId("response-body")).ToContainTextAsync("\"ok\": true");
    }

    [Fact]
    [Trait(E2ESettings.ModeTrait, E2ESettings.LoopbackMode)]
    public async Task Post_with_a_JSON_body_is_received_by_the_target_as_sent()
    {
        await using var target = new TargetServer();
        await target.StartAsync();
        var user = await TestUsers.CreateAsync();
        var page = await NewPageAsync();
        await Ui.SignInAsync(page, user.Email, user.Password);

        await Workspace.NewRequestInNewCollectionAsync(page);
        await page.GetByTestId("builder-method").SelectOptionAsync("POST");
        await Workspace.SetUrlAsync(page, new Uri(target.BaseAddress, "echo").ToString());
        await page.GetByTestId("builder-tab-body").ClickAsync();
        await page.GetByTestId("builder-body-type").SelectOptionAsync("raw");
        await page.GetByTestId("builder-body-raw").FillAsync("{\"hello\":\"e2e\"}");
        await Workspace.SendAsync(page);

        var echoed = page.GetByTestId("response-body");
        await Expect(echoed).ToContainTextAsync("method=POST");
        await Expect(echoed).ToContainTextAsync("body={\"hello\":\"e2e\"}");
    }

    [Fact]
    [Trait(E2ESettings.ModeTrait, E2ESettings.LoopbackMode)]
    public async Task Response_markup_is_shown_as_text_and_creates_no_element()
    {
        await using var target = new TargetServer();
        await target.StartAsync();
        var user = await TestUsers.CreateAsync();
        var page = await NewPageAsync();
        await Ui.SignInAsync(page, user.Email, user.Password);

        await Workspace.NewRequestInNewCollectionAsync(page);
        await Workspace.SetUrlAsync(page, new Uri(target.BaseAddress, "html").ToString());
        await Workspace.SendAsync(page);

        // The markup is on the page as characters. No element of that markup exists, and the injected handler did not run.
        await Expect(page.GetByTestId("response-body")).ToContainTextAsync("<p id=\"injected\">markup</p>");
        Assert.Equal(0, await page.Locator("#injected").CountAsync());
        Assert.Equal(0, await page.Locator("#injected-img").CountAsync());
        Assert.NotEqual("pwned", await page.TitleAsync());
    }

    [Fact]
    [Trait(E2ESettings.ModeTrait, E2ESettings.LoopbackMode)]
    public async Task Sent_request_is_in_history_and_reopens_in_the_builder_as_an_unsaved_draft()
    {
        await using var target = new TargetServer();
        await target.StartAsync();
        var user = await TestUsers.CreateAsync();
        var page = await NewPageAsync();
        await Ui.SignInAsync(page, user.Email, user.Password);

        await Workspace.NewRequestInNewCollectionAsync(page);
        var url = new Uri(target.BaseAddress, "json").ToString();
        await Workspace.SetUrlAsync(page, url);
        await Workspace.SendAsync(page);
        await Expect(page.GetByTestId("response-status")).ToContainTextAsync("200");

        await Ui.OpenAsync(page, "/history");
        await Expect(page.GetByTestId("history-list")).ToContainTextAsync("/json");
        await page.Locator("[data-testid^=history-row-]").First.ClickAsync();
        await Expect(page.GetByTestId("history-detail")).ToContainTextAsync("/json");
        await page.GetByTestId("history-open-builder").ClickAsync();

        await page.WaitForURLAsync(new Regex("reopen="));
        await Circuit.WaitForConnectedAsync(page);
        await Expect(page.GetByTestId("builder-url")).ToHaveValueAsync(url);

        // An unsaved draft: the page says it is not saved, and the builder's "unsaved changes" badge is shown.
        await Expect(page.GetByText("Opened from history as a new request", new() { Exact = false })).ToBeVisibleAsync();
        await Expect(page.GetByTestId("builder-dirty")).ToBeVisibleAsync();
    }

    [Fact]
    [Trait(E2ESettings.ModeTrait, E2ESettings.StrictMode)]
    public async Task Default_configuration_refuses_a_loopback_target_with_the_reason()
    {
        await using var target = new TargetServer();
        await target.StartAsync();
        var user = await TestUsers.CreateAsync();
        var page = await NewPageAsync();
        await Ui.SignInAsync(page, user.Email, user.Password);

        await Workspace.NewRequestInNewCollectionAsync(page);
        await Workspace.SetUrlAsync(page, new Uri(target.BaseAddress, "json").ToString());
        await Workspace.SendAsync(page);

        var outcome = page.GetByTestId("response-outcome");
        await Expect(outcome).ToContainTextAsync("Request blocked");
        await Expect(outcome).ToContainTextAsync("does not reach private, loopback or link-local addresses");
        Assert.Equal(0, await page.GetByTestId("response-status").CountAsync());
    }

    [Fact]
    public async Task Link_local_metadata_address_is_refused_in_every_configuration()
    {
        var user = await TestUsers.CreateAsync();
        var page = await NewPageAsync();
        await Ui.SignInAsync(page, user.Email, user.Password);

        await Workspace.NewRequestInNewCollectionAsync(page);
        await Workspace.SetUrlAsync(page, "http://169.254.169.254/latest/meta-data/");
        await Workspace.SendAsync(page);

        var outcome = page.GetByTestId("response-outcome");
        await Expect(outcome).ToContainTextAsync("Request blocked");
        await Expect(outcome).ToContainTextAsync("does not reach private, loopback or link-local addresses");
    }
}
