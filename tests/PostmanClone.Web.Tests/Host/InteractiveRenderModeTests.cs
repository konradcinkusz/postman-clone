using System.Text.RegularExpressions;
using PostmanClone.TestKit;

namespace PostmanClone.Web.Tests.Host;

/// <summary>
/// Without the render mode on both roots every page is static SSR: buttons do nothing and no error is raised
/// (BLAZOR-WEB-APP §3). The first two tests read the root component from disk; the last reads the live response.
/// </summary>
public sealed class InteractiveRenderModeTests(WebHostFactory factory) : IClassFixture<WebHostFactory>
{
    private static readonly string AppRazor = Path.Combine(RepoRoot.Find(), "src", "PostmanClone.Web", "Components", "App.razor");

    [Fact]
    public void HeadOutlet_carries_the_interactive_server_render_mode()
    {
        Assert.Matches(@"<HeadOutlet\s+@rendermode=""InteractiveServer""\s*/>", File.ReadAllText(AppRazor));
    }

    [Fact]
    public void Routes_carries_the_interactive_server_render_mode()
    {
        Assert.Matches(@"<Routes\s+@rendermode=""InteractiveServer""\s*/>", File.ReadAllText(AppRazor));
    }

    [Fact]
    public async Task The_home_page_is_rendered_as_interactive_components_not_static_html()
    {
        using var client = factory.CreateClient();

        var html = await client.GetStringAsync("/", TestContext.Current.CancellationToken);

        // Interactive components are emitted with Blazor's component markers; static SSR emits none.
        Assert.Contains("<!--Blazor:", html);
    }
}
