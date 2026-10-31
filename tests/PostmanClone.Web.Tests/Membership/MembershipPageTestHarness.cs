using System.Security.Claims;
using Bunit;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.Extensions.DependencyInjection;
using PostmanClone.TestKit;
using PostmanClone.Web.Auth;
using PostmanClone.Web.Membership;
using PostmanClone.Web.Tests.Auth;

namespace PostmanClone.Web.Tests.Membership;

/// <summary>
/// The members pages rendered by bUnit, with real objects behind them: a real session in the real store, a real
/// <see cref="CircuitSession"/> and <see cref="MembershipService"/>, and the fake authservice behind the real client.
/// The page sees the principal the session holds, as it does in a circuit.
/// </summary>
internal static class MembershipPageTestHarness
{
    public static BunitContext Create(AuthHarness harness, ClaimsPrincipal principal)
    {
        var context = new BunitContext();
        var state = new WorkspaceState();
        context.Services.AddSingleton(state);
        context.Services.AddSingleton<AuthenticationStateProvider>(new FixedAuthentication(principal));
        context.Services.AddSingleton<AntiforgeryStateProvider>(new StubAntiforgery());
        var session = new CircuitSession(state, harness.Refresher);
        context.Services.AddSingleton(session);
        context.Services.AddSingleton(new MembershipService(state, session, harness.Client));
        return context;
    }

    /// <summary>Signs the known address in through the fake and returns the session's own principal.</summary>
    public static async Task<ClaimsPrincipal> SignedInAsync(AuthHarness harness, Guid organization, string role, CancellationToken cancellationToken)
    {
        harness.Auth.Join(organization, "Acme", role);
        var session = await harness.OpenSessionAsync(cancellationToken);
        return session.Principal;
    }

    private sealed class FixedAuthentication(ClaimsPrincipal principal) : AuthenticationStateProvider
    {
        public override Task<AuthenticationState> GetAuthenticationStateAsync() => Task.FromResult(new AuthenticationState(principal));
    }

    private sealed class StubAntiforgery : AntiforgeryStateProvider
    {
        public override AntiforgeryRequestToken GetAntiforgeryToken() => new("test-token", "__RequestVerificationToken");
    }
}
