using System.Text.Json;
using Microsoft.Playwright;
using PostmanClone.Web.E2E.Support;
using static Microsoft.Playwright.Assertions;

namespace PostmanClone.Web.E2E.Journeys;

/// <summary>
/// J5 (WP26): an environment with a plain variable and a secret. The secret is typed, saved and reloaded, and its value never
/// appears anywhere in the page (ADR-0006, ADR-0025). The second journey sends with the secret in a bearer header: the target
/// receives it, and the response, the history and the page show it masked.
/// </summary>
public sealed class EnvironmentJourneys : Journey
{
    private const string MaskedValue = "••••••";

    // The badge text is what the page says for a stored secret; matched without case, since CSS may change the casing.
    private static readonly System.Text.RegularExpressions.Regex StoredState = new("stored", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

    [Fact]
    public async Task Secret_variable_is_never_rendered_after_typing_saving_or_reloading()
    {
        var user = await TestUsers.CreateAsync();
        var page = await NewPageAsync();
        await Ui.SignInAsync(page, user.Email, user.Password);

        var environment = $"E2E Env {Guid.NewGuid():N}";
        var secret = $"sek-{Guid.NewGuid():N}";
        await Ui.OpenAsync(page, "/environments");
        await EnvironmentSteps.CreateWithSecretAsync(page, environment, secret);

        // Typed (not yet saved), saved, and loaded again: the secret text is in none of them.
        await Expect(page.GetByTestId("env-notice")).ToBeVisibleAsync();
        Assert.DoesNotContain(secret, await page.ContentAsync());
        await Expect(page.GetByTestId("env-var-state-1")).ToContainTextAsync(StoredState);

        await Ui.OpenAsync(page, "/environments");
        await page.GetByTestId("env-list").GetByText(environment, new() { Exact = true }).ClickAsync();
        await Expect(page.GetByTestId("env-var-state-1")).ToContainTextAsync(StoredState);
        Assert.DoesNotContain(secret, await page.ContentAsync());
        await Expect(page.GetByTestId("env-var-key-0")).ToHaveValueAsync("base");
    }

    [Fact]
    [Trait(E2ESettings.ModeTrait, E2ESettings.LoopbackMode)]
    public async Task Bearer_secret_reaches_the_target_but_is_masked_in_the_response_and_in_history()
    {
        await using var target = new TargetServer();
        await target.StartAsync();
        var user = await TestUsers.CreateAsync();
        var page = await NewPageAsync();
        await Ui.SignInAsync(page, user.Email, user.Password);

        var environment = $"E2E Auth {Guid.NewGuid():N}";
        var secret = $"tok-{Guid.NewGuid():N}";
        await Ui.OpenAsync(page, "/environments");
        await EnvironmentSteps.CreateWithSecretAsync(page, environment, secret, baseUrl: target.BaseAddress.ToString().TrimEnd('/'));

        await Ui.OpenAsync(page, "/");
        await Workspace.NewRequestInNewCollectionAsync(page);
        await page.GetByTestId("environment-selector").SelectOptionAsync(new SelectOptionValue { Label = environment });
        await Workspace.SetUrlAsync(page, "{{base}}/echo");
        await page.GetByTestId("builder-tab-auth").ClickAsync();
        await page.GetByTestId("builder-auth-type").SelectOptionAsync("bearer");
        await page.GetByTestId("auth-bearer").FillAsync("{{token}}");
        await Workspace.SendAsync(page);

        // The target's echo carries the real header, so the sender did send the secret. The panel shows it masked.
        var body = page.GetByTestId("response-body");
        await Expect(body).ToContainTextAsync("Authorization=Bearer " + MaskedValue);
        Assert.DoesNotContain(secret, await page.ContentAsync());

        await Ui.OpenAsync(page, "/history");
        await Expect(page.GetByTestId("history-list")).ToBeVisibleAsync();
        await page.Locator("[data-testid^=history-row-]").First.ClickAsync();
        await Expect(page.GetByTestId("history-detail")).ToBeVisibleAsync();
        Assert.DoesNotContain(secret, await page.ContentAsync());
    }

}
