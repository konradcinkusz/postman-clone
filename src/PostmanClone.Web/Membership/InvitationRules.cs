using System.ComponentModel.DataAnnotations;
using PostmanClone.ServiceDefaults.Authentication;
using PostmanClone.Web.Api;

namespace PostmanClone.Web.Membership;

/// <summary>
/// The rules of the members page and the invitation flow (WP24, docs/AUTH.md). The page, the endpoints and the tests all
/// read them from here, so they cannot disagree. Plain code: no renderer and no HTTP.
/// </summary>
public static class InvitationRules
{
    /// <summary>
    /// The roles the Web offers when inviting. authservice also accepts Owner, which the plan keeps out of this app: only
    /// an Owner may grant it, and the Web does not offer a role it would only have to refuse.
    /// </summary>
    public static readonly IReadOnlyList<string> InvitableRoles = [WorkspaceRoles.Member, WorkspaceRoles.Admin];

    /// <summary>The longest address authservice takes (<c>InviteMemberRequest</c>, 256 characters).</summary>
    public const int MaximumEmailLength = 256;

    /// <summary>The longest invitation token authservice takes (<c>AcceptInvitationRequest</c>, 100 characters).</summary>
    public const int MaximumTokenLength = 100;

    /// <summary>Owners and Admins may invite and list invitations. The Web checks this before it calls authservice, which checks it again.</summary>
    public static bool CanInvite(string? role) => role is WorkspaceRoles.Owner or WorkspaceRoles.Admin;

    public static bool IsInvitableRole(string? role) => role is not null && InvitableRoles.Contains(role, StringComparer.Ordinal);

    /// <summary>The address as the form gives it, trimmed; null when it is empty, too long or not an address.</summary>
    public static string? NormalizeEmail(string? email)
    {
        var trimmed = email?.Trim();
        return trimmed is { Length: > 0 } && trimmed.Length <= MaximumEmailLength && new EmailAddressAttribute().IsValid(trimmed)
            ? trimmed
            : null;
    }

    /// <summary>
    /// The message code for a failed invite. The status decides, and the text of a 400 tells apart the two reasons
    /// authservice gives for one. The texts are the v0.3.4 ones (<c>OrganizationsController.cs</c>, docs/AUTH.md).
    /// </summary>
    public static string InviteFailureCode<T>(OrganizationAnswer<T> answer) => (answer.Status, answer.Error) switch
    {
        (403, _) => MembershipCodes.Forbidden,
        (400, { } text) when text.Contains("already a member", StringComparison.OrdinalIgnoreCase) => MembershipCodes.AlreadyMember,
        (400, { } text) when text.Contains("already been sent", StringComparison.OrdinalIgnoreCase) => MembershipCodes.Pending,
        (400, _) => MembershipCodes.Invalid,
        _ => MembershipCodes.Unavailable,
    };

    /// <summary>
    /// The message code for a failed accept. A 403 is the unverified-address refusal (the only 403 the accept gives), a
    /// 404 is an unknown token or one for another address, and a 400 says whether it expired or was used already.
    /// </summary>
    public static string AcceptFailureCode<T>(OrganizationAnswer<T> answer) => (answer.Status, answer.Error) switch
    {
        (403, _) => MembershipCodes.VerifyEmail,
        (404, _) => MembershipCodes.NotFound,
        (400, { } text) when text.Contains("expired", StringComparison.OrdinalIgnoreCase) => MembershipCodes.Expired,
        (400, { } text) when text.Contains("already been accepted", StringComparison.OrdinalIgnoreCase) => MembershipCodes.Used,
        (400, _) => MembershipCodes.Invalid,
        _ => MembershipCodes.Unavailable,
    };
}

/// <summary>The codes the membership redirects carry in their URLs. Only codes go in a URL, never a token or an address.</summary>
public static class MembershipCodes
{
    public const string Forbidden = "forbidden";
    public const string AlreadyMember = "member";
    public const string Pending = "pending";
    public const string Invalid = "invalid";
    public const string Unavailable = "unavailable";
    public const string Missing = "missing";
    public const string NotFound = "notfound";
    public const string Expired = "expired";
    public const string Used = "used";
    public const string VerifyEmail = "verify";
    public const string Read = "read";
    public const string Ended = "ended";
    public const string Invited = "invited";
    public const string Joined = "joined";
    public const string AlreadyIn = "already";
}
