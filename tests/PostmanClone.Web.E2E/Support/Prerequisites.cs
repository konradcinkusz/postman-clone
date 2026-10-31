namespace PostmanClone.Web.E2E.Support;

/// <summary>
/// The stack and the browser must be there before a journey starts. When one is missing, the journey is SKIPPED with the
/// reason, unless POSTMANCLONE_E2E_REQUIRED=1, in which case it FAILS. A skipped journey is never counted as passed
/// (docs/TESTING.md).
/// </summary>
public static class Prerequisites
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(5) };

    /// <summary>Checks the Web and authservice are answering their health endpoints. Throws or skips otherwise.</summary>
    public static async Task RequireStackAsync()
    {
        var missing = new List<string>();
        foreach (var (name, uri) in new[]
                 {
                     ("the Web", new Uri(E2ESettings.WebBaseUrl, "/health")),
                     ("authservice", new Uri(E2ESettings.AuthBaseUrl, "/health/ready")),
                     ("CollectionsService", new Uri(E2ESettings.CollectionsBaseUrl, "/health")),
                 })
        {
            if (!await IsOkAsync(uri))
            {
                missing.Add($"{name} at {uri}");
            }
        }

        if (missing.Count > 0)
        {
            Fail("The local stack is not answering: " + string.Join("; ", missing)
                 + ". Start it with `scripts/run-local-stack.sh up` and `E2E_SENDER_LOOPBACK=1` for the loopback journeys.");
        }
    }

    /// <summary>Stops the journey: a skip with the reason when local, a failure when POSTMANCLONE_E2E_REQUIRED=1.</summary>
    public static void Fail(string reason)
    {
        if (E2ESettings.Required)
        {
            throw new InvalidOperationException("E2E prerequisite missing (POSTMANCLONE_E2E_REQUIRED=1): " + reason);
        }

        Xunit.Assert.Skip(reason);
    }

    private static async Task<bool> IsOkAsync(Uri uri)
    {
        try
        {
            using var response = await Http.GetAsync(uri);
            return response.IsSuccessStatusCode;
        }
        catch (HttpRequestException)
        {
            return false;
        }
        catch (TaskCanceledException)
        {
            return false;
        }
    }
}
