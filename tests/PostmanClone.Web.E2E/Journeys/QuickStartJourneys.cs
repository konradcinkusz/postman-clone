using Microsoft.Playwright;
using PostmanClone.Web.E2E.Support;
using static Microsoft.Playwright.Assertions;

namespace PostmanClone.Web.E2E.Journeys;

/// <summary>
/// The README's five steps, as a journey (ADR-0030): start the estate (the stack is up, seeded), sign in as the seeded owner, pair the
/// agent on the Agent page, open the seeded local request, send it with the agent. It needs the local agent on loopback, which only the
/// local stack starts, so it runs where the agent journeys run.
/// </summary>
public sealed class QuickStartJourneys : Journey
{
    [Fact]
    [Trait(E2ESettings.ModeTrait, E2ESettings.LoopbackMode)]
    public async Task The_seeded_owner_pairs_the_agent_and_sends_the_seeded_local_request_through_it()
    {
        await AgentStack.RequireAsync();
        var page = await NewPageAsync();
        await Ui.SignInAsync(page, DemoLogins.OwnerEmail, DemoLogins.Password);

        await AgentSteps.PairThisPageAsync(page);
        await AgentSteps.BackToWorkspaceAsync(page);

        await Workspace.ExpandAsync(page, DemoLogins.Collection);
        await page.GetByTestId("tree").GetByText(DemoLogins.LocalRequest, new() { Exact = true }).ClickAsync();
        await Expect(page.GetByTestId("builder-url")).ToHaveValueAsync("http://localhost:5303/health");

        await AgentSteps.SendViaAgentAsync(page);
        await Expect(page.GetByTestId("response-status")).ToContainTextAsync("200");
    }
}
