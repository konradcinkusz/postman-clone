using PostmanClone.TestKit;

namespace PostmanClone.Web.Tests.Host;

/// <summary>Without the scoped-styles link every component rule is silently absent (BLAZOR-WEB-APP §3).</summary>
public sealed class StylesheetLinkTests(WebHostFactory factory) : IClassFixture<WebHostFactory>
{
    private static readonly string AppRazor = Path.Combine(RepoRoot.Find(), "src", "PostmanClone.Web", "Components", "App.razor");

    [Fact]
    public void App_links_the_scoped_styles_bundle_of_this_assembly()
    {
        Assert.Contains(@"<link rel=""stylesheet"" href=""@Assets[""PostmanClone.Web.styles.css""]"" />", File.ReadAllText(AppRazor));
    }

    [Fact]
    public async Task The_scoped_styles_bundle_and_the_token_stylesheet_are_served()
    {
        using var client = factory.CreateClient();

        using var scoped = await client.GetAsync("/PostmanClone.Web.styles.css", TestContext.Current.CancellationToken);
        using var tokens = await client.GetAsync("/app.css", TestContext.Current.CancellationToken);

        Assert.True(scoped.IsSuccessStatusCode, $"scoped bundle answered {(int)scoped.StatusCode}");
        Assert.True(tokens.IsSuccessStatusCode, $"token stylesheet answered {(int)tokens.StatusCode}");
    }
}
