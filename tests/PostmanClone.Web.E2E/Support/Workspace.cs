using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace PostmanClone.Web.E2E.Support;

/// <summary>The tree and builder steps the journeys share. Each drives the real control and waits on the state it produces.</summary>
public static class Workspace
{
    public static async Task CreateCollectionAsync(IPage page, string name)
    {
        await page.GetByTestId("tree-new-collection").ClickAsync();
        await page.GetByTestId("new-collection-name").FillAsync(name);
        await page.GetByTestId("new-collection-submit").ClickAsync();
        await Expect(page.GetByTestId("tree")).ToContainTextAsync(name);
    }

    /// <summary>Selects a tree row by its visible name, then waits for the selection panel to show it.</summary>
    public static async Task SelectAsync(IPage page, string name)
    {
        await page.GetByTestId("tree").GetByText(name, new() { Exact = true }).ClickAsync();
        await Expect(page.GetByTestId("selection-name")).ToHaveTextAsync(name);
    }

    public static async Task CreateFolderAsync(IPage page, string name)
    {
        await page.GetByTestId("action-new-folder").ClickAsync();
        await Expect(page.GetByTestId("folder-form")).ToBeVisibleAsync();
        await page.GetByTestId("folder-name").FillAsync(name);
        await page.GetByTestId("folder-submit").ClickAsync();
        await Expect(page.GetByTestId("folder-form")).ToBeHiddenAsync();
    }

    /// <summary>
    /// Expands a collapsed row so its children are in the tree, and waits for its toggle to read "Collapse". A row that is
    /// already open is left alone.
    /// </summary>
    public static async Task ExpandAsync(IPage page, string name)
    {
        var expand = page.GetByRole(AriaRole.Button, new() { Name = $"Expand {name}", Exact = true });
        if (await expand.CountAsync() > 0)
        {
            await expand.First.ClickAsync();
            await Expect(page.GetByRole(AriaRole.Button, new() { Name = $"Collapse {name}", Exact = true })).ToBeVisibleAsync();
        }
    }

    /// <summary>Creates an untitled request in the selected collection or folder; the builder opens on it.</summary>
    public static async Task<string> NewRequestAsync(IPage page)
    {
        await page.GetByTestId("action-new-request").ClickAsync();
        await Expect(page.GetByTestId("builder")).ToBeVisibleAsync();
        return await page.GetByTestId("request-title").InnerTextAsync();
    }

    /// <summary>Sets the URL in the builder's URL bar. The bar syncs the query table on each input.</summary>
    public static async Task SetUrlAsync(IPage page, string url)
    {
        await page.GetByTestId("builder-url").FillAsync(url);
    }

    /// <summary>Sends from the builder and waits for the response panel to show the outcome of this send.</summary>
    public static async Task SendAsync(IPage page)
    {
        await page.GetByTestId("builder-send").ClickAsync();
        await Expect(page.GetByTestId("response")).ToBeVisibleAsync();
    }

    /// <summary>A new collection with one untitled request in it, opened in the builder. Returns the request's name.</summary>
    public static async Task<string> NewRequestInNewCollectionAsync(IPage page)
    {
        var collection = $"Send {Guid.NewGuid():N}";
        await CreateCollectionAsync(page, collection);
        await SelectAsync(page, collection);
        return await NewRequestAsync(page);
    }
}
