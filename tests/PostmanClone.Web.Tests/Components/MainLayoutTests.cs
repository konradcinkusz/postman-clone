using Bunit;
using Microsoft.Extensions.DependencyInjection;
using PostmanClone.Web.Api;
using PostmanClone.Web.Components.Layout;

namespace PostmanClone.Web.Tests.Components;

/// <summary>The layout renders the backend states, the navigation and the page body (bUnit, no browser).</summary>
public sealed class MainLayoutTests
{
    [Fact]
    public void The_status_strip_shows_each_backend_with_its_state()
    {
        using var context = new BunitContext();
        context.Services.AddSingleton<IBackendStatusReader>(new FixedStatusReader(
            new BackendStatus("authservice", "healthy"),
            new BackendStatus("collections", BackendStates.Unreachable)));

        var cut = context.Render<MainLayout>(parameters => parameters.Add(p => p.Body, builder => builder.AddContent(0, "page body")));

        cut.WaitForState(() => cut.FindAll(".status-list li").Count == 2);
        Assert.Contains("authservice: healthy", cut.Find("[data-testid='status-authservice']").TextContent);
        Assert.Contains("badge-success", cut.Find("[data-testid='status-authservice'] span").ClassList);
        Assert.Contains("badge-danger", cut.Find("[data-testid='status-collections'] span").ClassList);
    }

    [Fact]
    public void The_navigation_renders_every_page_in_order_and_the_body_is_in_main()
    {
        using var context = new BunitContext();
        context.Services.AddSingleton<IBackendStatusReader>(new FixedStatusReader());

        var cut = context.Render<MainLayout>(parameters => parameters.Add(p => p.Body, builder => builder.AddContent(0, "page body")));

        var hrefs = cut.FindAll("nav a").Select(link => link.GetAttribute("href")).ToArray();
        Assert.Equal(["/", "/environments", "/history", "/import", "/workspace", "/about"], hrefs);
        Assert.Contains("page body", cut.Find("main#main").TextContent);
    }

    [Fact]
    public void A_failed_status_read_is_an_alert_not_an_exception()
    {
        using var context = new BunitContext();
        context.Services.AddSingleton<IBackendStatusReader>(new ThrowingStatusReader());

        var cut = context.Render<MainLayout>(parameters => parameters.Add(p => p.Body, builder => builder.AddContent(0, "page body")));

        cut.WaitForState(() => cut.FindAll("[role='alert']").Count == 1);
        Assert.Contains("could not be read", cut.Find("[role='alert']").TextContent);
    }

    private sealed class FixedStatusReader(params BackendStatus[] statuses) : IBackendStatusReader
    {
        public Task<IReadOnlyList<BackendStatus>> ReadAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<BackendStatus>>(statuses);
    }

    private sealed class ThrowingStatusReader : IBackendStatusReader
    {
        public Task<IReadOnlyList<BackendStatus>> ReadAsync(CancellationToken cancellationToken) =>
            throw new InvalidOperationException("simulated read failure");
    }
}
