namespace PostmanClone.Seeder;

/// <summary>
/// Reads an invitation token from authservice's log. authservice v0.3.4 has no API that returns the token: with no email provider
/// it writes it to its log, and only when it runs in Development (docs/AUTH.md, Members and invitations). The log is therefore the
/// "mailbox" of a local estate. The token is a credential: it is returned to the caller and never printed.
/// </summary>
internal static class InvitationLog
{
    private const string Marker = "Invitation token: ";

    /// <summary>The newest token logged for <paramref name="email"/>, or null when none is readable (absent file, or "[withheld]").</summary>
    public static string? TokenFor(string? path, string email)
    {
        if (path is null || !File.Exists(path))
        {
            return null;
        }

        string text;
        using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
        using (var reader = new StreamReader(stream))
        {
            text = reader.ReadToEnd();
        }

        string? token = null;
        foreach (var line in text.Split('\n'))
        {
            if (!line.Contains(email, StringComparison.OrdinalIgnoreCase) || !line.Contains(Marker, StringComparison.Ordinal))
            {
                continue;
            }

            var rest = line[(line.IndexOf(Marker, StringComparison.Ordinal) + Marker.Length)..];
            var end = rest.IndexOf(". ", StringComparison.Ordinal);
            var candidate = (end >= 0 ? rest[..end] : rest).Trim().TrimEnd('.');
            token = candidate is { Length: > 0 } and not "[withheld]" ? candidate : null;
        }

        return token;
    }
}
