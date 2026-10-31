using System.Security.Claims;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using PostmanClone.Web.Auth;
using PostmanClone.Web.History;
using PostmanClone.Web.Tests.Auth;
using PostmanClone.Web.Tests.History;

namespace PostmanClone.Web.Tests.HistoryPages;

/// <summary>
/// The history pages rendered by bUnit with the real shell, state and session behind them, and the fake history service in place of
/// ExecutionService. The signed-in principal names one workspace, as the session of a signed-in user does.
/// </summary>
internal static class HistoryPageHarness
{
    /// <summary>A context for the history page and the panel: the history services, the fake clock and the circuit's session.</summary>
    public static BunitContext Create(AuthHarness harness, ClaimsPrincipal principal, FakeHistoryClient history, FakeTimeProvider clock)
    {
        var context = new BunitContext();
        var state = Applied(principal);
        AddHistoryServices(context, state, new CircuitSession(state, harness.Refresher), history, clock);
        return context;
    }

    /// <summary>
    /// Renders a page at an address. Query parameters reach a page only through the navigation manager (bUnit), so the tests
    /// navigate to the address the page is linked from, and render it there.
    /// </summary>
    public static IRenderedComponent<T> RenderAt<T>(BunitContext context, string address) where T : IComponent
    {
        context.Services.GetRequiredService<NavigationManager>().NavigateTo(address);
        return context.Render<T>();
    }

    /// <summary>The workspace state for a principal, as a signed-in circuit holds it (the one workspace of the token).</summary>
    public static WorkspaceState Applied(ClaimsPrincipal principal)
    {
        var state = new WorkspaceState();
        state.Apply(principal);
        return state;
    }

    /// <summary>
    /// Adds the history services to a context. It must be called before the context resolves a service (bUnit refuses a registration
    /// after that), so the callers pass the state and the session they already hold.
    /// </summary>
    public static void AddHistoryServices(BunitContext context, WorkspaceState state, CircuitSession session, FakeHistoryClient history, FakeTimeProvider clock)
    {
        var signal = new HistoryChangeSignal();
        context.Services.AddSingleton<TimeProvider>(clock);
        context.Services.AddSingleton(new HistoryShell(state, session, history));
        context.Services.AddSingleton(signal);
        context.Services.AddSingleton<IHistoryChanged>(signal);
    }
}
