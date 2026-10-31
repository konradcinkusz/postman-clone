using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace PostmanClone.Web.E2E.Support;

/// <summary>Steps that several journeys share. Each one drives the real HTML form or the real control, never a shortcut.</summary>
public static class Ui
{
    /// <summary>Opens a page and waits for its circuit, so the next step is never sent to a render that is about to be replaced.</summary>
    public static async Task OpenAsync(IPage page, string path)
    {
        await page.GotoAsync(path);
        await Circuit.WaitForConnectedAsync(page);
    }

    /// <summary>Signs in through the sign-in form and waits for the next page (home or the no-workspace page).</summary>
    public static async Task SignInAsync(IPage page, string email, string password)
    {
        await OpenAsync(page, "/sign-in");
        await page.GetByTestId("sign-in-email").FillAsync(email);
        await page.GetByTestId("sign-in-password").FillAsync(password);
        await page.GetByTestId("sign-in-submit").ClickAsync();
        await page.WaitForURLAsync(url => new Uri(url).AbsolutePath is "/" or "/no-workspace");
        await Circuit.WaitForConnectedAsync(page);
    }

    /// <summary>Signs out with the header's sign-out form and waits for the sign-in page.</summary>
    public static async Task SignOutAsync(IPage page)
    {
        await Circuit.WaitForConnectedAsync(page);
        await page.GetByTestId("sign-out").ClickAsync();
        await page.WaitForURLAsync(url => new Uri(url).AbsolutePath == "/sign-in");
    }

    /// <summary>True when the page is no wider than the viewport: no horizontal scroll (390 px checks).</summary>
    public static async Task<(int ScrollWidth, int ClientWidth)> MeasureAsync(IPage page)
    {
        var measured = await page.EvaluateAsync<int[]>("() => [document.documentElement.scrollWidth, document.documentElement.clientWidth]");
        return (measured[0], measured[1]);
    }

    public static Task<int> ScrollWidthAsync(IPage page) => page.EvaluateAsync<int>("() => document.documentElement.scrollWidth");
}
