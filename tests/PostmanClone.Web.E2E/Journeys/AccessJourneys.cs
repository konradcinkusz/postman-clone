using Microsoft.Playwright;
using PostmanClone.Web.E2E.Support;
using static Microsoft.Playwright.Assertions;

namespace PostmanClone.Web.E2E.Journeys;

/// <summary>
/// J9 to J11 and the members page (WP26): another workspace's request is a not-found answer, not data (CONTRACTS §0,
/// ADR-0004); a user with two organizations sees the Multiple page; the members page lists the workspace's members.
/// Each journey creates its own accounts, so no journey can see another's data.
/// </summary>
public sealed class AccessJourneys : Journey
{
    [Fact]
    public async Task Another_workspaces_request_is_a_not_found_answer_not_data()
    {
        var owner = await TestUsers.CreateAsync();
        var stranger = await TestUsers.CreateAsync();
        var privateUrl = $"http://example.test/private/{Guid.NewGuid():N}";

        // The owner creates a request in the owner's workspace and copies its address from the selection panel.
        var ownerPage = await NewPageAsync();
        await Ui.SignInAsync(ownerPage, owner.Email, owner.Password);
        var (collectionName, requestName) = await Workspace.NewRequestInNewCollectionAsync(ownerPage);
        await Workspace.SetUrlAsync(ownerPage, privateUrl);
        await ownerPage.GetByTestId("builder-save").ClickAsync();
        await Expect(ownerPage.GetByTestId("builder-dirty")).ToBeHiddenAsync();

        await Ui.OpenAsync(ownerPage, "/");
        await Workspace.ExpandAsync(ownerPage, collectionName);
        await Workspace.SelectAsync(ownerPage, requestName);
        var curlPath = await ownerPage.GetByTestId("action-copy-curl").GetAttributeAsync("href");
        Assert.NotNull(curlPath);

        // The stranger has a workspace of their own and asks for the owner's request by its address.
        var strangerPage = await NewPageAsync();
        await Ui.SignInAsync(strangerPage, stranger.Email, stranger.Password);
        await Ui.OpenAsync(strangerPage, curlPath);

        await Expect(strangerPage.GetByTestId("curl-error")).ToContainTextAsync("no longer exists");
        Assert.Equal(0, await strangerPage.GetByTestId("curl-command").CountAsync());
        Assert.DoesNotContain(privateUrl, await strangerPage.ContentAsync());
    }

    [Fact]
    public async Task A_user_with_two_organizations_sees_the_multiple_page_and_no_workspace_data()
    {
        var user = await TestUsers.CreateAsync(organizations: 2);
        var page = await NewPageAsync();
        await Ui.SignInAsync(page, user.Email, user.Password);

        await Expect(page.GetByTestId("workspace-multiple")).ToBeVisibleAsync();
        Assert.Equal(0, await page.GetByTestId("tree-new-collection").CountAsync());
    }

    [Fact]
    public async Task Members_page_lists_the_signed_in_owner_of_the_workspace()
    {
        var user = await TestUsers.CreateAsync(organizations: 1);
        var page = await NewPageAsync();
        await Ui.SignInAsync(page, user.Email, user.Password);

        await Ui.OpenAsync(page, "/workspace");
        await Expect(page.GetByTestId("members-section")).ToBeVisibleAsync();
        await Expect(page.GetByTestId("members-table")).ToContainTextAsync(user.Email);
    }
}
