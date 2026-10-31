using PostmanClone.Web.Components.Layout;

namespace PostmanClone.Web.Tests.Components;

/// <summary>The navigation order is a product decision (CONTRACTS §5, BLAZOR-WEB-APP §4): a later edit cannot reorder it silently.</summary>
public sealed class NavOrderTests
{
    [Fact]
    public void The_navigation_lists_the_routes_in_the_order_contracts_section_5_pins()
    {
        var routes = NavItems.All.Select(item => item.Href).ToArray();

        Assert.Equal(["/", "/environments", "/history", "/import", "/workspace", "/about"], routes);
    }

    [Fact]
    public void Every_navigation_item_has_a_label()
    {
        Assert.All(NavItems.All, item => Assert.False(string.IsNullOrWhiteSpace(item.Label)));
    }
}
