using System.Text.Json;
using Microsoft.Playwright;
using PostmanClone.Web.E2E.Support;
using static Microsoft.Playwright.Assertions;

namespace PostmanClone.Web.E2E.Journeys;

/// <summary>
/// The browser journeys through the real local agent (WP32, ADR-0026, ADR-0028), against the whole local estate: real authservice,
/// EnvironmentsService and ExecutionService, and the agent running on 127.0.0.1. The agent sends to a target on this machine. The
/// sentinel secret must be in the target and in no page, no history response and no agent output.
/// </summary>
public sealed class AgentJourneys : Journey
{
    private const string MaskedValue = "••••••";

    [Fact]
    [Trait(E2ESettings.ModeTrait, E2ESettings.LoopbackMode)]
    public async Task Pairing_from_the_agent_page_shows_Connected_in_the_top_bar()
    {
        await AgentStack.RequireAsync();
        var user = await TestUsers.CreateAsync();
        var page = await NewPageAsync();
        await Ui.SignInAsync(page, user.Email, user.Password);

        await AgentSteps.PairThisPageAsync(page);

        await Expect(AgentSteps.TopBarStatus(page)).ToContainTextAsync("Connected");
        // The pairing is recorded as one row for this user in this workspace (the Agent page lists it).
        await Expect(page.Locator("[data-testid^=agent-installation-]")).ToHaveCountAsync(1);
    }

    [Fact]
    [Trait(E2ESettings.ModeTrait, E2ESettings.LoopbackMode)]
    public async Task Send_through_the_agent_uses_the_environment_and_shows_the_secret_masked_everywhere()
    {
        await AgentStack.RequireAsync();
        await using var target = new TargetServer();
        await target.StartAsync();
        var user = await TestUsers.CreateAsync();
        var page = await NewPageAsync();
        await Ui.SignInAsync(page, user.Email, user.Password);

        var environment = $"E2E Agent {Guid.NewGuid():N}";
        var secret = $"agt-{Guid.NewGuid():N}";
        await Ui.OpenAsync(page, "/environments");
        await EnvironmentSteps.CreateWithSecretAsync(page, environment, secret, baseUrl: TargetRoot(target));

        await AgentSteps.PairThisPageAsync(page);
        await AgentSteps.BackToWorkspaceAsync(page);
        await Workspace.NewRequestInNewCollectionAsync(page);
        await page.GetByTestId("environment-selector").SelectOptionAsync(new SelectOptionValue { Label = environment });
        // `base` is the environment's plain variable (the product has no collection-level variables); `token` is its secret.
        await Workspace.SetUrlAsync(page, "{{base}}/echo");
        await page.GetByTestId("builder-tab-auth").ClickAsync();
        await page.GetByTestId("builder-auth-type").SelectOptionAsync("bearer");
        await page.GetByTestId("auth-bearer").FillAsync("{{token}}");
        // The page posts the masked run to the Web after the agent answers. The response is awaited by state, not by time.
        var runPosted = page.WaitForResponseAsync(response => response.Url.EndsWith("/agent/runs", StringComparison.Ordinal));
        await AgentSteps.SendViaAgentAsync(page);
        // The send is over when the status is shown: the panel is visible while the send is still in flight.
        await Expect(page.GetByTestId("response-status")).ToContainTextAsync("200");
        var run = await runPosted;
        if (run.Status != 201)
        {
            // Names only: the values of the headers and cookies are not printed.
            var headers = string.Join(", ", (await run.Request.AllHeadersAsync()).Keys);
            var cookies = string.Join(", ", (await page.Context.CookiesAsync()).Select(cookie => cookie.Name));
            var shape = DescribeShape(run.Request.PostData);
            Assert.Fail($"The run record was not recorded: POST /agent/runs answered {run.Status} ({await run.TextAsync()}). "
                + $"Request headers: {headers}. Cookies on the page: {cookies}. Body shape: {shape}.");
        }

        // The target received the real secret, through the agent. Only the page is masked.
        Assert.Equal("Bearer " + secret, target.LastAuthorization);
        await Expect(page.GetByTestId("response-sender")).ToContainTextAsync("your agent on this computer");
        await Expect(page.GetByTestId("response-body")).ToContainTextAsync("Authorization=Bearer " + MaskedValue);
        Assert.DoesNotContain(secret, await page.ContentAsync());

        // The run record reached the server: an agent row with the masked template. The Web posts it after the send.
        var list = await Poll.UntilAsync(async () =>
            {
                var json = await HistoryApi.ListJsonAsync(user.AccessToken, TestContext.Current.CancellationToken);
                return HistoryApi.Rows(json).Any(row => row.Source == "agent") ? json : null;
            },
            "an agent row in the server history",
            TestContext.Current.CancellationToken);
        Assert.DoesNotContain(secret, list);
        Assert.Contains("{{base}}/echo", list);

        // The history page shows the same entry with the secret masked.
        await Ui.OpenAsync(page, "/history");
        await Expect(page.GetByTestId("history-list")).ToBeVisibleAsync();
        await page.Locator("[data-testid^=history-row-]").First.ClickAsync();
        await Expect(page.GetByTestId("history-detail")).ToBeVisibleAsync();
        Assert.DoesNotContain(secret, await page.ContentAsync());

        // The agent's own output (stdout and stderr, in the stack's log) never held the secret.
        Assert.True(File.Exists(AgentStack.LogPath), $"The agent log {AgentStack.LogPath} is missing, so the check cannot pass.");
        Assert.DoesNotContain(secret, AgentStack.LogText());
    }

    [Fact]
    [Trait(E2ESettings.ModeTrait, E2ESettings.StrictMode)]
    public async Task With_the_agent_connected_the_agent_reaches_a_loopback_target_and_the_server_path_refuses_it()
    {
        await AgentStack.RequireAsync();
        await using var target = new TargetServer();
        await target.StartAsync();
        var user = await TestUsers.CreateAsync();
        var page = await NewPageAsync();
        await Ui.SignInAsync(page, user.Email, user.Password);

        await AgentSteps.PairThisPageAsync(page);
        await AgentSteps.BackToWorkspaceAsync(page);
        await Workspace.NewRequestInNewCollectionAsync(page);
        await Workspace.SetUrlAsync(page, new Uri(target.BaseAddress, "json").ToString());

        // The agent is not subject to the server's loopback rule: it sends.
        await AgentSteps.SendViaAgentAsync(page);
        await Expect(page.GetByTestId("response-status")).ToContainTextAsync("200");

        // The server, in this configuration, refuses the same target with the reason.
        await page.GetByTestId("builder-send-target").SelectOptionAsync("server");
        await Workspace.SendAsync(page);
        var outcome = page.GetByTestId("response-outcome");
        await Expect(outcome).ToContainTextAsync("Request blocked");
        await Expect(outcome).ToContainTextAsync("does not reach private, loopback or link-local addresses");

        // Refusing on the server path does not unpair the agent.
        await Expect(AgentSteps.TopBarStatus(page)).ToContainTextAsync("Connected");
    }

    [Fact]
    [Trait(E2ESettings.ModeTrait, E2ESettings.LoopbackMode)]
    public async Task Revoking_the_installation_makes_the_next_agent_send_ask_for_a_new_pairing()
    {
        await AgentStack.RequireAsync();
        await using var target = new TargetServer();
        await target.StartAsync();
        var user = await TestUsers.CreateAsync();
        var page = await NewPageAsync();
        await Ui.SignInAsync(page, user.Email, user.Password);

        await AgentSteps.PairThisPageAsync(page);
        await AgentSteps.InstallationRevokeButtons(page).First.ClickAsync();
        await Expect(page.GetByTestId("agent-notice")).ToBeVisibleAsync();
        await Expect(page.Locator("[data-testid^=agent-installation-]").First).ToContainTextAsync("Revoked");

        // The revoke is a form post, so this page holds no pairing now. The agent still has its own: the top bar asks for a new pairing.
        await Expect(AgentSteps.TopBarStatus(page)).ToContainTextAsync("Pair again");

        // The next send through the agent says so, and the server does not get the request.
        await AgentSteps.BackToWorkspaceAsync(page);
        await Workspace.NewRequestInNewCollectionAsync(page);
        await Workspace.SetUrlAsync(page, new Uri(target.BaseAddress, "json").ToString());
        await AgentSteps.SendViaAgentAsync(page);
        await Expect(page.GetByTestId("response-error")).ToContainTextAsync("Pair again");
        Assert.Equal(0, await page.GetByTestId("response-status").CountAsync());
    }

    /// <summary>The property names and value kinds of a JSON body, one level down: no value is included.</summary>
    private static string DescribeShape(string? body)
    {
        if (string.IsNullOrEmpty(body))
        {
            return "no body";
        }

        try
        {
            using var document = JsonDocument.Parse(body);
            return string.Join(", ", document.RootElement.EnumerateObject().Select(property =>
                property.Value.ValueKind == JsonValueKind.Object
                    ? $"{property.Name}{{{string.Join(" ", property.Value.EnumerateObject().Select(inner => inner.Name))}}}"
                    : $"{property.Name}:{property.Value.ValueKind}"));
        }
        catch (JsonException)
        {
            return "not JSON";
        }
    }

    /// <summary>The target's root without its trailing slash, as an environment's base value.</summary>
    private static string TargetRoot(TargetServer target) => target.BaseAddress.ToString().TrimEnd('/');
}
