using Microsoft.Playwright;

namespace PostmanClone.Web.E2E.Support;

/// <summary>
/// The base of every journey. Each test instance gets its own Chromium and its own browser contexts, closed when the test ends,
/// so no journey can leak a cookie, a tab or a circuit into another (independence). The stack and the browser are checked
/// before the first page opens; a missing one skips or fails the journey with the reason (Prerequisites).
/// </summary>
public abstract class Journey : IAsyncLifetime
{
    static Journey()
    {
        // Assertions wait for their state up to this long; the stack's stalls are the reason it is above the 5 s default.
        Assertions.SetDefaultExpectTimeout(15_000);
    }

    private IPlaywright? _playwright;
    private IBrowser? _browser;
    private readonly List<IBrowserContext> _contexts = [];

    public virtual ValueTask InitializeAsync() => ValueTask.CompletedTask;

    /// <summary>A fresh context on the Web. Viewport and colour scheme are the only settings a journey chooses.</summary>
    protected async Task<IBrowserContext> NewContextAsync(ViewportSize? viewport = null, ColorScheme? colorScheme = null)
    {
        await Prerequisites.RequireStackAsync();
        _browser ??= await LaunchAsync();

        var context = await _browser.NewContextAsync(new BrowserNewContextOptions
        {
            BaseURL = E2ESettings.WebBaseUrl.ToString(),
            ViewportSize = viewport ?? new ViewportSize { Width = 1280, Height = 900 },
            ColorScheme = colorScheme ?? ColorScheme.Light,
            Locale = "en-GB",
        });
        _contexts.Add(context);
        return context;
    }

    /// <summary>
    /// A page in a new context. The timeouts are longer than Playwright's defaults because the local stack's sign-in calls
    /// authservice, which can stall for tens of seconds in a session (TestUsers). A journey that waits that long still
    /// asserts its outcome; the wait is bounded, not a sleep.
    /// </summary>
    protected async Task<IPage> NewPageAsync(ViewportSize? viewport = null, ColorScheme? colorScheme = null)
    {
        var context = await NewContextAsync(viewport, colorScheme);
        var page = await context.NewPageAsync();
        page.SetDefaultTimeout(30_000);
        page.SetDefaultNavigationTimeout(60_000);
        return page;
    }

    private async Task<IBrowser> LaunchAsync()
    {
        var executable = BrowserLocator.Find();
        if (executable is null)
        {
            Prerequisites.Fail(BrowserLocator.MissingBrowserReason);
        }

        _playwright = await Playwright.CreateAsync();
        // --no-sandbox: the sandboxed Chromium cannot start as root in the session container and in CI's container jobs.
        // The browser only ever opens the local stack, so the sandbox is not what this suite checks.
        return await _playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            ExecutablePath = executable,
            Headless = true,
            Args = ["--no-sandbox"],
        });
    }

    public virtual async ValueTask DisposeAsync()
    {
        foreach (var context in _contexts)
        {
            await context.CloseAsync();
        }

        if (_browser is not null)
        {
            await _browser.CloseAsync();
        }

        _playwright?.Dispose();
    }
}
