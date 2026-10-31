using Microsoft.Playwright;
using PostmanClone.Web.E2E.Support;
using static Microsoft.Playwright.Assertions;

namespace PostmanClone.Web.E2E.Journeys;

/// <summary>
/// J12 and J13 (WP26): the four main pages fit a 390 px phone without a horizontal scroll, and the dark colour scheme applies
/// the dark tokens (UI-STYLE, ADR-0009: colours only through the CSS tokens, both themes).
/// </summary>
public sealed class PresentationJourneys : Journey
{
    private static readonly ViewportSize Phone = new() { Width = 390, Height = 844 };

    [Fact]
    public async Task Shell_builder_environments_and_history_have_no_horizontal_scroll_at_390_pixels()
    {
        var user = await TestUsers.CreateAsync();
        var page = await NewPageAsync(viewport: Phone);
        await Ui.SignInAsync(page, user.Email, user.Password);

        await AssertNoHorizontalScrollAsync(page, "shell");

        await Workspace.NewRequestInNewCollectionAsync(page);
        await AssertNoHorizontalScrollAsync(page, "builder");

        await Ui.OpenAsync(page, "/environments");
        await Expect(page.GetByTestId("environments-title")).ToBeVisibleAsync();
        await AssertNoHorizontalScrollAsync(page, "environments");

        await Ui.OpenAsync(page, "/history");
        await Expect(page.GetByTestId("history-page")).ToBeVisibleAsync();
        await AssertNoHorizontalScrollAsync(page, "history");
    }

    [Fact]
    public async Task Dark_colour_scheme_applies_the_dark_tokens_and_light_applies_the_light_ones()
    {
        var user = await TestUsers.CreateAsync();

        var dark = await NewPageAsync(colorScheme: ColorScheme.Dark);
        await Ui.SignInAsync(dark, user.Email, user.Password);
        var darkBackground = await dark.EvaluateAsync<string>("() => getComputedStyle(document.body).backgroundColor");
        var darkScheme = await dark.EvaluateAsync<string>("() => getComputedStyle(document.documentElement).colorScheme");
        Assert.Equal("dark", darkScheme);
        Assert.Equal("rgb(14, 19, 26)", darkBackground); // --surface-page, dark

        var light = await NewPageAsync(colorScheme: ColorScheme.Light);
        await Ui.SignInAsync(light, user.Email, user.Password);
        var lightBackground = await light.EvaluateAsync<string>("() => getComputedStyle(document.body).backgroundColor");
        Assert.Equal("rgb(244, 246, 249)", lightBackground); // --surface-page, light
    }

    private static async Task AssertNoHorizontalScrollAsync(IPage page, string name)
    {
        var (scrollWidth, clientWidth) = await Ui.MeasureAsync(page);
        Assert.True(scrollWidth <= clientWidth, $"{name}: the page is {scrollWidth} px wide in a {clientWidth} px viewport");
    }
}
