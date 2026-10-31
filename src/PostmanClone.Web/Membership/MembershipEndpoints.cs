using PostmanClone.ServiceDefaults.RateLimiting;
using PostmanClone.ServiceDefaults.Authentication;
using PostmanClone.Web.Api;
using PostmanClone.Web.Auth;

namespace PostmanClone.Web.Membership;

/// <summary>
/// The two form posts of the members flow (WP24): inviting a member and accepting an invitation. They are minimal
/// endpoints, like the sign-in forms, so the antiforgery check applies and the answer is a real redirect. Both read the
/// workspace from the token's claims, never from the form, and both answer with a code in the redirect, never a token.
/// </summary>
public static class MembershipEndpoints
{
    public static WebApplication MapMembershipEndpoints(this WebApplication app)
    {
        app.MapPost("/workspace/invitations", InviteAsync).RequireAuthorization();
        app.MapPost("/accept-invitation", AcceptAsync).RequireAuthorization().RequireRateLimiting(RateLimitPolicies.Auth);
        return app;
    }

    private static async Task<IResult> InviteAsync(HttpContext http, IFormCollection form, AuthServiceClient authservice, TokenRefresher refresher, CancellationToken cancellationToken)
    {
        var sessionId = http.User.FindFirst(ServerSession.SessionIdClaim)?.Value;
        if (sessionId is null)
        {
            return SignInEnded(http);
        }

        // Zero or several organizations have no workspace to invite into: the page that explains them is the answer.
        var resolution = WorkspaceClaims.Resolve(http.User);
        if (!resolution.IsActive)
        {
            return Redirect(http, WorkspaceRouting.NoWorkspacePath);
        }

        // A Member's form is refused here, before authservice is asked, as the page would not have offered it.
        if (!InvitationRules.CanInvite(resolution.Role))
        {
            return Redirect(http, $"/workspace?error={MembershipCodes.Forbidden}");
        }

        var email = InvitationRules.NormalizeEmail(form["email"].ToString());
        var role = form["role"].ToString();
        if (email is null || !InvitationRules.IsInvitableRole(role))
        {
            return Redirect(http, $"/workspace?error={MembershipCodes.Invalid}");
        }

        var accessToken = await refresher.AccessTokenAsync(sessionId, touch: true, cancellationToken);
        if (accessToken is null)
        {
            return SignInEnded(http);
        }

        var answer = await authservice.InviteMemberAsync(accessToken, resolution.WorkspaceId!.Value.ToString(), email, role, cancellationToken);
        return answer.Outcome == AuthOutcome.Succeeded
            ? Redirect(http, $"/workspace?notice={MembershipCodes.Invited}")
            : Redirect(http, $"/workspace?error={InvitationRules.InviteFailureCode(answer)}");
    }

    private static async Task<IResult> AcceptAsync(HttpContext http, IFormCollection form, AuthServiceClient authservice, TokenRefresher refresher, CancellationToken cancellationToken)
    {
        var sessionId = http.User.FindFirst(ServerSession.SessionIdClaim)?.Value;
        if (sessionId is null)
        {
            return SignInEnded(http);
        }

        var token = form["token"].ToString().Trim();
        if (token.Length is 0)
        {
            return Redirect(http, $"/accept-invitation?error={MembershipCodes.Missing}");
        }

        if (token.Length > InvitationRules.MaximumTokenLength)
        {
            return Redirect(http, $"/accept-invitation?error={MembershipCodes.Invalid}");
        }

        // The organizations the user held before this request. authservice answers 200 for an invitation to one of them
        // already, so the answer alone cannot say whether this request joined anything.
        var before = http.User.FindAll(WorkspaceClaims.OrganizationClaim).Select(claim => claim.Value).ToArray();

        var accessToken = await refresher.AccessTokenAsync(sessionId, touch: true, cancellationToken);
        if (accessToken is null)
        {
            return SignInEnded(http);
        }

        var answer = await authservice.AcceptInvitationAsync(accessToken, token, cancellationToken);
        if (answer.Outcome != AuthOutcome.Succeeded || answer.Value is null)
        {
            return Redirect(http, $"/accept-invitation?error={InvitationRules.AcceptFailureCode(answer)}");
        }

        // The new membership is in authservice now. The session must reread it, or the next page still shows the old workspaces.
        if (!await refresher.RefreshNowAsync(sessionId, cancellationToken))
        {
            return SignInEnded(http);
        }

        var joined = before.Contains(answer.Value.OrganizationId, StringComparer.OrdinalIgnoreCase)
            ? MembershipCodes.AlreadyIn
            : MembershipCodes.Joined;
        return Redirect(http, $"/workspace?notice={joined}");
    }

    private static IResult SignInEnded(HttpContext http) =>
        Redirect(http, ReturnUrlPolicy.SignInPath(ReturnUrlPolicy.Fallback, MembershipCodes.Ended));

    private static IResult Redirect(HttpContext http, string location)
    {
        http.Response.Headers.CacheControl = "private, no-store";
        return Results.Redirect(location);
    }
}
