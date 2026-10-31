namespace PostmanClone.Web.Membership;

/// <summary>
/// The texts the members page and the accept page show for the codes their redirects carry (<see cref="MembershipCodes"/>).
/// A code is the only thing a URL carries, so a message never names the address or the token.
/// </summary>
public static class MembershipMessages
{
    private static readonly Dictionary<string, string> Failures = new(StringComparer.Ordinal)
    {
        [MembershipCodes.Forbidden] = "Only an Owner or an Admin can invite members.",
        [MembershipCodes.AlreadyMember] = "That person is already a member of this workspace.",
        [MembershipCodes.Pending] = "An invitation to that address is already waiting for an answer.",
        [MembershipCodes.Invalid] = "Enter an email address of up to 256 characters and choose a role.",
        [MembershipCodes.Unavailable] = "The sign-in service could not be reached. Try again shortly.",
        [MembershipCodes.Missing] = "This link does not carry an invitation. Open the link from your invitation again.",
        [MembershipCodes.NotFound] = "This invitation was not found for your signed-in address. Sign in with the address it was sent to.",
        [MembershipCodes.Expired] = "This invitation has expired. Ask for a new one.",
        [MembershipCodes.Used] = "This invitation has already been used.",
        [MembershipCodes.VerifyEmail] = "Confirm your email address before you accept an invitation.",
        [MembershipCodes.Read] = "The workspace could not be read. Try again shortly.",
        [MembershipCodes.Ended] = "Your session has ended. Sign in again.",
    };

    private static readonly Dictionary<string, string> Notices = new(StringComparer.Ordinal)
    {
        [MembershipCodes.Invited] = "Invitation created. authservice sends its email, or only logs it when no email provider is configured.",
        [MembershipCodes.Joined] = "You have joined the workspace.",
        [MembershipCodes.AlreadyIn] = "You are already a member of this workspace.",
    };

    /// <summary>The message for an error code, else for a notice code, else null. <c>IsError</c> picks the alert colour.</summary>
    public static (string Text, bool IsError)? For(string? error, string? notice)
    {
        if (error is not null && Failures.TryGetValue(error, out var failure))
        {
            return (failure, true);
        }

        if (notice is not null && Notices.TryGetValue(notice, out var success))
        {
            return (success, false);
        }

        return null;
    }
}
