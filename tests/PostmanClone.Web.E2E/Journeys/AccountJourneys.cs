using System.Text.RegularExpressions;
using Microsoft.Playwright;
using PostmanClone.Web.E2E.Support;
using static Microsoft.Playwright.Assertions;

namespace PostmanClone.Web.E2E.Journeys;

/// <summary>
/// J1 and J2 (WP26): sign-up through the register form, a workspace created on the no-workspace page, a wrong password
/// refused, the right one accepted, the session cookie kept away from browser script, sign-out, and a protected page that
/// sends a signed-out visitor back to sign-in. Each journey uses its own generated account.
/// </summary>
public sealed class AccountJourneys : Journey
{
    [Fact]
    public async Task Register_through_the_form_then_create_a_workspace_and_reach_home()
    {
        var page = await NewPageAsync();
        var email = $"e2e-{Guid.NewGuid():N}@example.test";
        var password = $"Pw-{Guid.NewGuid():N}-Aa1"; // generated for this run, not stored anywhere
        var workspace = $"E2E Workspace {Guid.NewGuid():N}";

        await Ui.OpenAsync(page, "/register");
        await page.GetByTestId("register-email").FillAsync(email);
        await page.GetByTestId("register-password").FillAsync(password);
        await page.GetByTestId("register-accept-terms").CheckAsync();
        await page.GetByTestId("register-accept-privacy").CheckAsync();
        await page.GetByTestId("register-submit").ClickAsync();

        // A new account has no workspace yet, so the Web sends it to the page that creates one.
        await page.WaitForURLAsync(url => new Uri(url).AbsolutePath == "/no-workspace");
        await Circuit.WaitForConnectedAsync(page);
        await page.GetByTestId("create-workspace-name").FillAsync(workspace);
        await page.GetByTestId("create-workspace-submit").ClickAsync();

        await page.WaitForURLAsync(url => new Uri(url).AbsolutePath == "/");
        await Circuit.WaitForConnectedAsync(page);
        await Expect(page.GetByTestId("tree-new-collection")).ToBeVisibleAsync();
        await Expect(page.GetByRole(AriaRole.Heading, new() { Level = 1 })).ToHaveTextAsync(workspace);
    }

    [Fact]
    public async Task Wrong_password_is_refused_and_leaves_no_session_then_sign_in_sign_out_and_protected_redirect()
    {
        var user = await TestUsers.CreateAsync(organizations: 1);
        var page = await NewPageAsync();
        var context = page.Context;

        await Ui.OpenAsync(page, "/sign-in");
        await page.GetByTestId("sign-in-email").FillAsync(user.Email);
        await page.GetByTestId("sign-in-password").FillAsync($"Wrong-{Guid.NewGuid():N}-Aa1");
        await page.GetByTestId("sign-in-submit").ClickAsync();

        await Expect(page).ToHaveURLAsync(new Regex("error=invalid"));
        await Expect(page.GetByTestId("sign-in-message")).ToContainTextAsync("Email or password is not right.");
        Assert.Empty(await SessionCookiesAsync(context));

        await Ui.SignInAsync(page, user.Email, user.Password);
        await Expect(page.GetByTestId("tree-new-collection")).ToBeVisibleAsync();

        // The token pair stays on the server: the session cookie is HttpOnly and not visible to browser script.
        var cookies = await SessionCookiesAsync(context);
        Assert.Single(cookies);
        Assert.True(cookies[0].HttpOnly, "the session cookie must be HttpOnly");
        var documentCookies = await page.EvaluateAsync<string>("() => document.cookie");
        Assert.DoesNotContain(cookies[0].Name, documentCookies);

        await Ui.SignOutAsync(page);
        Assert.Empty(await SessionCookiesAsync(context));

        await Ui.OpenAsync(page, "/");
        await page.WaitForURLAsync(url => new Uri(url).AbsolutePath == "/sign-in");
        await Expect(page.GetByTestId("sign-in-form")).ToBeVisibleAsync();
    }

    private static async Task<List<BrowserContextCookiesResult>> SessionCookiesAsync(IBrowserContext context)
    {
        var all = await context.CookiesAsync();
        return all.Where(cookie => cookie.Name.StartsWith("pc.session", StringComparison.Ordinal)).ToList();
    }
}
