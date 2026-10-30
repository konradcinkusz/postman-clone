using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Server;
using Microsoft.Extensions.Logging;

namespace PostmanClone.Web.Auth;

/// <summary>
/// Asks the session every five minutes whether the circuit may stay signed in (ADR-0005, ADR-0013). When the session has
/// ended, the circuit's principal is no longer valid, so the user's pages stop showing and <c>RedirectToLogin</c> sends
/// them to sign in. A session that ends does not wait for a page load to be noticed.
/// </summary>
public sealed class RevalidatingAuthenticationStateProvider(ILoggerFactory loggers, SessionRevalidator revalidator)
    : RevalidatingServerAuthenticationStateProvider(loggers)
{
    protected override TimeSpan RevalidationInterval => SessionRevalidator.Interval;

    protected override async Task<bool> ValidateAuthenticationStateAsync(AuthenticationState authenticationState, CancellationToken cancellationToken)
    {
        var sessionId = authenticationState.User.FindFirst(ServerSession.SessionIdClaim)?.Value;
        return sessionId is not null && await revalidator.IsStillValidAsync(sessionId, cancellationToken);
    }
}
