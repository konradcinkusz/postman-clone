using Bunit;
using PostmanClone.Web.Components.Builder;

namespace PostmanClone.Web.Tests.EnvironmentsUi;

/// <summary>The URL bar marks a variable the chosen environment does not define (WP21, WP19 acceptance). Nothing is marked before a choice.</summary>
public sealed class UrlBarKnownTests
{
    [Fact]
    public void With_an_environment_chosen_an_undefined_variable_is_marked_unresolved_and_a_defined_one_is_not()
    {
        using var context = new BunitContext();

        var cut = context.Render<UrlBar>(parameters => parameters
            .Add(bar => bar.Value, "https://{{host}}/{{path}}")
            .Add(bar => bar.Known, new HashSet<string>(StringComparer.Ordinal) { "host" }));

        Assert.Contains("var-known", cut.Find("[data-testid='builder-var-host']").GetAttribute("class"));
        Assert.Contains("var-unresolved", cut.Find("[data-testid='builder-var-path']").GetAttribute("class"));
        Assert.Contains("path", cut.Find("[data-testid='builder-unresolved']").TextContent);
        Assert.DoesNotContain("host", cut.Find("[data-testid='builder-unresolved']").TextContent);
    }

    [Fact]
    public void With_no_environment_chosen_nothing_is_marked_unresolved()
    {
        using var context = new BunitContext();

        var cut = context.Render<UrlBar>(parameters => parameters
            .Add(bar => bar.Value, "https://{{host}}/{{path}}")
            .Add(bar => bar.Known, null));

        Assert.Empty(cut.FindAll("[data-testid='builder-unresolved']"));
        Assert.DoesNotContain("var-unresolved", cut.Markup);
    }
}
