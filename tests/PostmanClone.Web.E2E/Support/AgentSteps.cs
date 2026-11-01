using System.Text.Json;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace PostmanClone.Web.E2E.Support;

/// <summary>
/// The agent steps the journeys share (WP31, ADR-0028). Pairing runs through the page's own controls: a code is created, the page
/// pairs with the agent on loopback, and the top bar reads Connected. The pairing token lives in the page's circuit, so a full
/// page load loses it: the journeys move inside the app (BackToWorkspaceAsync) once they are paired.
/// </summary>
public static class AgentSteps
{
    private static readonly System.Text.RegularExpressions.Regex Connected = new("^connected$");

    /// <summary>
    /// Creates a pairing code on the Agent page and pairs this page with the agent. Returns once the top bar reads Connected.
    /// A journey that needs the agent calls this first, after it has signed in.
    /// </summary>
    public static async Task PairThisPageAsync(IPage page)
    {
        await Ui.OpenAsync(page, "/agent");
        await Circuit.WaitForConnectedAsync(page);
        await page.GetByTestId("agent-create-code").ClickAsync();
        await Expect(page.GetByTestId("agent-pair-code")).ToBeVisibleAsync();

        await page.GetByTestId("agent-pair").ClickAsync();
        await Expect(TopBarStatus(page)).ToHaveAttributeAsync("data-state", Connected);
        await Expect(page.GetByTestId("agent-page-status")).ToHaveAttributeAsync("data-state", Connected);
    }

    /// <summary>The top bar's agent status (on every page of the shell).</summary>
    public static ILocator TopBarStatus(IPage page) => page.GetByTestId("agent-status");

    /// <summary>
    /// Moves to the workspace home by the brand link, inside the same circuit, so the pairing still holds. A full load to "/"
    /// would start a new circuit and lose the token.
    /// </summary>
    public static async Task BackToWorkspaceAsync(IPage page)
    {
        await page.GetByRole(AriaRole.Link, new() { Name = "PostmanClone", Exact = true }).ClickAsync();
        // The tree's "New collection" is on the home page whatever is selected; "New request" appears only with a selection.
        await Expect(page.GetByTestId("tree-new-collection")).ToBeVisibleAsync();
    }

    /// <summary>Chooses the agent as the sender in the builder, then sends. The response panel shows who sent it.</summary>
    public static async Task SendViaAgentAsync(IPage page)
    {
        await page.GetByTestId("builder-send-target").SelectOptionAsync("agent");
        await Workspace.SendAsync(page);
    }

    /// <summary>The Agent page's installation rows, one per pairing (the revoke buttons carry their ids).</summary>
    public static ILocator InstallationRevokeButtons(IPage page) => page.Locator("[data-testid^=agent-revoke-]");
}

/// <summary>
/// Reads the user's history from ExecutionService directly (the same call the page makes). The history UI does not show a
/// row's source, so the journeys read it here: an agent run is recorded with source "agent" (CONTRACTS §4).
/// </summary>
public static class HistoryApi
{
    private static readonly HttpClient Http = new() { BaseAddress = E2ESettings.ExecutionBaseUrl, Timeout = TimeSpan.FromSeconds(30) };

    /// <summary>The raw JSON of the list. The caller reads its items; the raw text is what the masking check reads.</summary>
    public static async Task<string> ListJsonAsync(string accessToken, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/history?page=1&pageSize=50");
        request.Headers.Authorization = new("Bearer", accessToken);
        using var response = await Http.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(cancellationToken);
    }

    /// <summary>The source of each list item, with its masked URL template, in the order the service returns them.</summary>
    public static List<(string Source, string Url)> Rows(string listJson)
    {
        using var document = JsonDocument.Parse(listJson);
        return document.RootElement.GetProperty("items").EnumerateArray()
            .Select(item => (item.GetProperty("source").GetString()!, item.GetProperty("url").GetString()!))
            .ToList();
    }

    /// <summary>The raw JSON of one entry.</summary>
    public static async Task<string> EntryJsonAsync(string accessToken, Guid id, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/history/{id}");
        request.Headers.Authorization = new("Bearer", accessToken);
        using var response = await Http.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(cancellationToken);
    }
}
