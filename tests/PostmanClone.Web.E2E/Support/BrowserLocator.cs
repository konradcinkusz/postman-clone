namespace PostmanClone.Web.E2E.Support;

/// <summary>
/// Finds the Chromium this run drives. Order: E2E_CHROMIUM_PATH; then a `chromium-*` folder under the sessions' copy
/// (/opt/pw-browsers) or under Playwright's user cache (CI installs it there with `playwright install chromium`).
/// The search never downloads: a missing browser is a prerequisite failure with an instruction.
/// </summary>
public static class BrowserLocator
{
    public static string? Find()
    {
        if (!string.IsNullOrWhiteSpace(E2ESettings.ChromiumPath))
        {
            return File.Exists(E2ESettings.ChromiumPath) ? E2ESettings.ChromiumPath : null;
        }

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        foreach (var root in new[] { "/opt/pw-browsers", Path.Combine(home, ".cache", "ms-playwright") })
        {
            if (!Directory.Exists(root))
            {
                continue;
            }

            foreach (var folder in Directory.GetDirectories(root, "chromium-*").OrderByDescending(p => p, StringComparer.Ordinal))
            {
                foreach (var layout in new[] { "chrome-linux64", "chrome-linux" })
                {
                    var executable = Path.Combine(folder, layout, "chrome");
                    if (File.Exists(executable))
                    {
                        return executable;
                    }
                }
            }
        }

        return null;
    }

    public const string MissingBrowserReason =
        "No Chromium found. Set E2E_CHROMIUM_PATH to a Chromium executable, or run `playwright install chromium` "
        + "(the sessions have one at /opt/pw-browsers). The suite never downloads a browser.";
}
