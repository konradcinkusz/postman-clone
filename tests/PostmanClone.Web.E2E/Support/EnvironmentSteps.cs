using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace PostmanClone.Web.E2E.Support;

/// <summary>The environment steps the journeys share (ADR-0025): the plain variable and the secret are typed through the page's own controls.</summary>
public static class EnvironmentSteps
{
    /// <summary>Creates an environment with a plain variable `base` and a secret `token`, and saves it.</summary>
    public static async Task CreateWithSecretAsync(IPage page, string name, string secret, string baseUrl = "http://example.test")
    {
        await page.GetByTestId("env-new-name").FillAsync(name);
        await page.GetByTestId("env-create").ClickAsync();
        await Expect(page.GetByTestId("env-name")).ToHaveValueAsync(name);

        await page.GetByTestId("env-var-add").ClickAsync();
        await page.GetByTestId("env-var-key-0").FillAsync("base");
        await page.GetByTestId("env-var-value-0").FillAsync(baseUrl);

        await page.GetByTestId("env-var-add").ClickAsync();
        await page.GetByTestId("env-var-key-1").FillAsync("token");
        await page.GetByTestId("env-var-secret-1").CheckAsync();
        await page.GetByTestId("env-var-secret-value-1").FillAsync(secret);
        await Expect(page.GetByTestId("env-var-typed-1")).ToBeVisibleAsync();
        Assert.DoesNotContain(secret, await page.ContentAsync());

        await page.GetByTestId("env-save").ClickAsync();
        await Expect(page.GetByTestId("env-dirty")).ToBeHiddenAsync();
    }
}
