using Microsoft.Playwright;
using PostmanClone.Web.E2E.Support;
using static Microsoft.Playwright.Assertions;

namespace PostmanClone.Web.E2E.Journeys;

/// <summary>
/// J3 (WP26): a collection, a folder and a request are created through the tree and the builder, the request is saved,
/// the page is reloaded, and the request is still there with its URL.
/// </summary>
public sealed class WorkspaceJourneys : Journey
{
    [Fact]
    public async Task Collection_folder_and_request_are_created_saved_and_still_there_after_reload()
    {
        var user = await TestUsers.CreateAsync(organizations: 1);
        var page = await NewPageAsync();
        await Ui.SignInAsync(page, user.Email, user.Password);

        var collection = $"Orders {Guid.NewGuid():N}";
        var folder = $"Auth {Guid.NewGuid():N}";
        var url = "http://example.test/api/orders";

        await Workspace.CreateCollectionAsync(page, collection);
        await Workspace.SelectAsync(page, collection);
        await Workspace.CreateFolderAsync(page, folder);

        // The tree reads the workspace from the server on load, so a reload shows what the next visit shows.
        await Ui.OpenAsync(page, "/");
        await Workspace.ExpandAsync(page, collection);
        await Expect(page.GetByTestId("tree")).ToContainTextAsync(folder);
        await Workspace.SelectAsync(page, folder);
        var requestName = await Workspace.NewRequestAsync(page);
        await Workspace.SetUrlAsync(page, url);
        await page.GetByTestId("builder-save").ClickAsync();
        await Expect(page.GetByTestId("builder-dirty")).Not.ToBeVisibleAsync();

        await Ui.OpenAsync(page, "/");
        await Workspace.ExpandAsync(page, collection);
        await Workspace.ExpandAsync(page, folder);
        await Expect(page.GetByTestId("tree")).ToContainTextAsync(requestName);

        await page.GetByTestId("tree").GetByText(requestName, new() { Exact = true }).ClickAsync();
        await Expect(page.GetByTestId("builder-url")).ToHaveValueAsync(url);
    }
}
