using Microsoft.Playwright;

namespace PostmanClone.Web.E2E.Support;

/// <summary>
/// Waits for the Blazor Server circuit before a journey touches an interactive page. The server render is replaced when the
/// circuit connects, so a field filled or a button clicked before that is lost without an error (it happened in the
/// sign-in journey: the second sign-in silently did not POST). Two signals, both state rather than time: the client runtime
/// has started (`Blazor._internal.navigationManager` exists), and, where the page has a status strip, the strip has been
/// replaced by the server's answer. Pages without a strip (the sign-in page) skip the second check.
/// </summary>
public static class Circuit
{
    public static async Task WaitForConnectedAsync(IPage page)
    {
        await page.WaitForFunctionAsync("() => !!(window.Blazor && window.Blazor._internal && window.Blazor._internal.navigationManager)");
        await page.WaitForFunctionAsync(
            "() => { const strip = document.querySelector('[data-testid=status-strip]');"
            + " return !strip || !/Reading backend status/.test(strip.innerText); }");
    }
}
