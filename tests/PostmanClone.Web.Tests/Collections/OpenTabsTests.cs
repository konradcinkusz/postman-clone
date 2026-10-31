using PostmanClone.Web.Collections;

namespace PostmanClone.Web.Tests.Collections;

/// <summary>The open request tabs: open, activate, close, the dirty flag and the limit of 20 (WP18).</summary>
public sealed class OpenTabsTests
{
    [Fact]
    public void Opening_a_request_makes_its_tab_the_active_one()
    {
        var tabs = new OpenTabs();
        var refund = Guid.NewGuid();

        Assert.Equal(OpenTabResult.Opened, tabs.Open(refund, "Refund"));

        Assert.Equal(refund, tabs.ActiveRequestId);
        Assert.Equal("Refund", tabs.Active!.Title);
        Assert.False(tabs.Active.Dirty);
    }

    [Fact]
    public void Opening_a_request_that_already_has_a_tab_activates_that_tab()
    {
        var tabs = new OpenTabs();
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        tabs.Open(first, "First");
        tabs.Open(second, "Second");

        Assert.Equal(OpenTabResult.Activated, tabs.Open(first, "First"));

        Assert.Equal(2, tabs.Tabs.Count);
        Assert.Equal(first, tabs.ActiveRequestId);
    }

    [Fact]
    public void The_twenty_first_tab_is_refused_and_nothing_changes()
    {
        var tabs = new OpenTabs();
        var opened = Enumerable.Range(1, OpenTabs.MaximumOpen).Select(index => tabs.Open(Guid.NewGuid(), $"Request {index}")).ToList();
        var extra = Guid.NewGuid();

        Assert.All(opened, result => Assert.Equal(OpenTabResult.Opened, result));
        Assert.Equal(OpenTabResult.Full, tabs.Open(extra, "Too many"));
        Assert.Equal(OpenTabs.MaximumOpen, tabs.Tabs.Count);
        Assert.DoesNotContain(tabs.Tabs, tab => tab.RequestId == extra);
    }

    [Fact]
    public void Closing_the_active_tab_activates_the_one_that_takes_its_place()
    {
        var tabs = new OpenTabs();
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var c = Guid.NewGuid();
        tabs.Open(a, "A");
        tabs.Open(b, "B");
        tabs.Open(c, "C");
        tabs.Activate(b);

        Assert.Equal(CloseTabResult.Closed, tabs.Close(b));

        Assert.Equal(c, tabs.ActiveRequestId);
    }

    [Fact]
    public void Closing_the_last_tab_leaves_nothing_active()
    {
        var tabs = new OpenTabs();
        var only = Guid.NewGuid();
        tabs.Open(only, "Only");

        tabs.Close(only);

        Assert.Empty(tabs.Tabs);
        Assert.Null(tabs.ActiveRequestId);
        Assert.Null(tabs.Active);
    }

    [Fact]
    public void A_dirty_tab_stays_open_until_its_changes_are_discarded()
    {
        var tabs = new OpenTabs();
        var draft = Guid.NewGuid();
        tabs.Open(draft, "Draft");
        tabs.SetDirty(draft, true);

        Assert.Equal(CloseTabResult.Dirty, tabs.Close(draft));
        Assert.Single(tabs.Tabs);

        Assert.Equal(CloseTabResult.Closed, tabs.Close(draft, discardChanges: true));
        Assert.Empty(tabs.Tabs);
    }

    [Fact]
    public void Retitling_keeps_the_tab_in_its_place_with_its_state()
    {
        var tabs = new OpenTabs();
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        tabs.Open(first, "Old name");
        tabs.Open(second, "Other");
        tabs.SetDirty(first, true);

        Assert.True(tabs.Retitle(first, "New name"));

        Assert.Equal(first, tabs.Tabs[0].RequestId);
        Assert.Equal("New name", tabs.Tabs[0].Title);
        Assert.True(tabs.Tabs[0].Dirty);
    }

    [Fact]
    public void Operations_on_a_request_without_a_tab_change_nothing()
    {
        var tabs = new OpenTabs();
        var stranger = Guid.NewGuid();

        Assert.Equal(OpenTabResult.NotOpen, tabs.Activate(stranger));
        Assert.Equal(CloseTabResult.NotOpen, tabs.Close(stranger));
        Assert.False(tabs.SetDirty(stranger, true));
        Assert.False(tabs.Retitle(stranger, "x"));
        Assert.Empty(tabs.Tabs);
    }
}
