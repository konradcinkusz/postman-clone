using PostmanClone.TestKit;

namespace PostmanClone.Web.Tests.Components;

/// <summary>
/// The authorization shape of the pages, read from disk (WP12, BLAZOR-WEB-APP §7): every page is authorized unless it is
/// on the explicit anonymous list, and no component reaches for the HTTP context.
/// </summary>
public sealed class AuthPagePinTests
{
    private static readonly string Components = Path.Combine(RepoRoot.Find(), "src", "PostmanClone.Web", "Components");

    [Fact]
    public void Only_the_sign_in_sign_up_and_legal_pages_are_anonymous()
    {
        var anonymous = Directory.EnumerateFiles(Path.Combine(Components, "Pages"), "*.razor", SearchOption.AllDirectories)
            .Where(file => File.ReadAllText(file).Contains("@attribute [AllowAnonymous]"))
            .Select(file => Path.GetRelativePath(Path.Combine(Components, "Pages"), file).Replace('\\', '/'))
            .Order()
            .ToArray();

        Assert.Equal(["Legal/Privacy.razor", "Legal/Terms.razor", "Register.razor", "SignIn.razor"], anonymous);
    }

    [Fact]
    public void Every_page_is_authorized_by_the_folder_attribute()
    {
        Assert.Contains("@attribute [Authorize]", File.ReadAllText(Path.Combine(Components, "Pages", "_Imports.razor")));
    }

    [Fact]
    public void The_routes_check_authorization_and_send_a_signed_out_visitor_to_sign_in()
    {
        var routes = File.ReadAllText(Path.Combine(Components, "Routes.razor"));

        Assert.Contains("<CascadingAuthenticationState>", routes);
        Assert.Contains("<AuthorizeRouteView", routes);
        Assert.Contains("<RedirectToLogin />", routes);
    }

    [Fact]
    public void No_component_reads_the_HTTP_context()
    {
        var offenders = Directory.EnumerateFiles(Components, "*.*", SearchOption.AllDirectories)
            .Where(file => file.EndsWith(".razor", StringComparison.Ordinal) || file.EndsWith(".cs", StringComparison.Ordinal))
            .Where(file => File.ReadAllText(file).Contains("HttpContext", StringComparison.Ordinal))
            .Select(file => Path.GetRelativePath(Components, file))
            .ToArray();

        Assert.Empty(offenders);
    }
}
