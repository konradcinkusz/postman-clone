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
        await AuthBudget.AcquireAsync(TestContext.Current.CancellationToken);
        var answered = page.WaitForResponseAsync(response => response.Request.Method == "POST" && new Uri(response.Url).AbsolutePath == "/sign-in");
        await page.GetByTestId("sign-in-submit").ClickAsync();
        // Say what the server answered when the sign-in does not land. Without this, a refused or queued sign-in
        // reads as a generic URL timeout (the failure seen in CI on the strict set).
        IResponse answer;
        try
        {
            answer = await answered;
        }
        catch (TimeoutException)
        {
            throw new InvalidOperationException("The sign-in form was submitted, but no answer came from the Web within the timeout. " +
                "A request queued in authservice's sign-in window looks like this.");
        }

        if (answer.Status is 429 or >= 500)
        {
            throw new InvalidOperationException($"The sign-in form was answered {answer.Status}: {await answer.TextAsync()}");
        }

        await page.WaitForURLAsync(url => new Uri(url).AbsolutePath is "/" or "/no-workspace");
        await Circuit.WaitForConnectedAsync(page);
    }

    /// <summary>Signs out with the header's sign-out form and waits for the sign-in page.</summary>
    public static async Task SignOutAsync(IPage page)
    {
        await Circuit.WaitForConnectedAsync(page);
        await AuthBudget.AcquireAsync(TestContext.Current.CancellationToken);
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
