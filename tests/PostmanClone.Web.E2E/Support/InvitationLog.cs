namespace PostmanClone.Web.E2E.Support;

/// <summary>
/// The invitation token for an address, read from authservice's log. With no email provider the token is written to the log only
/// when the stack runs in Development (`AUTHSERVICE_ENVIRONMENT=Development`, docs/AUTH.md, Invitations); otherwise the log holds
/// `[withheld]`, and this returns null. The token is a credential: it is returned to the journey and never printed.
/// </summary>
public static class InvitationLog
{
    private const string Marker = "Invitation token: ";

    /// <summary>The newest token logged for <paramref name="email"/>, or null when none is readable.</summary>
    public static string? TokenFor(string email)
    {
        string? token = null;
        foreach (var line in StackLogs.ReadText("authservice.log").Split('\n'))
        {
            if (!line.Contains(email, StringComparison.OrdinalIgnoreCase) || !line.Contains(Marker, StringComparison.Ordinal))
            {
                continue;
            }

            var rest = line[(line.IndexOf(Marker, StringComparison.Ordinal) + Marker.Length)..];
            // The token ends at the sentence's full stop, which the log writes after the token.
            var end = rest.IndexOf(". ", StringComparison.Ordinal);
            var candidate = (end >= 0 ? rest[..end] : rest).Trim();
            token = candidate is { Length: > 0 } and not "[withheld]" ? candidate : null;
        }

        return token;
    }
}
