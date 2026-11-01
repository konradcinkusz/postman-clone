namespace PostmanClone.Sending.Assembly;

/// <summary>One header as it goes on the wire: a name and a value, both already expanded and checked.</summary>
public sealed record HeaderLine(string Name, string Value);

/// <summary>
/// Turns the enabled header rows into header lines. A row whose name or value would let the request carry a second
/// header (CR, LF, a control character, a name that is not an HTTP token) is refused with <c>invalidRequest</c> codes,
/// and so is a header the sender sets itself (CONTRACTS §4 sender rules).
/// </summary>
public static class HeaderBuilder
{
    private static readonly string[] ForbiddenNames =
    [
        "Host",
        "Content-Length",
        "Connection",
        "Transfer-Encoding",
        "Upgrade",
        "TE",
        "Expect",
    ];

    public static List<HeaderLine> Build(IEnumerable<KeyValueRow> rows, VariableInterpolator variables)
    {
        var headers = new List<HeaderLine>();
        foreach (var row in rows)
        {
            if (!row.Enabled || row.Key.Length == 0)
            {
                continue;
            }

            var name = AssemblyText.RequireWellFormed(variables.Expand(row.Key), "header name");
            var value = AssemblyText.RequireWellFormed(variables.Expand(row.Value), "header value");
            headers.Add(Checked(name, value));
        }

        return headers;
    }

    /// <summary>Checks one header and returns it. Used for header rows and for the auth helper alike.</summary>
    public static HeaderLine Checked(string name, string value)
    {
        if (!IsToken(name))
        {
            throw new RequestAssemblyException(RequestAssemblyException.InvalidHeaderName, "A header name is empty or is not an HTTP token.");
        }

        if (IsForbidden(name))
        {
            throw new RequestAssemblyException(RequestAssemblyException.ForbiddenHeader, "This header is set by the sender and cannot be set in a request.");
        }

        if (value.Any(IsForbiddenValueCharacter))
        {
            throw new RequestAssemblyException(RequestAssemblyException.InvalidHeaderValue, "A header value contains a control character.");
        }

        return new HeaderLine(name, value);
    }

    public static bool SameName(string left, string right) => string.Equals(left, right, StringComparison.OrdinalIgnoreCase);

    private static bool IsForbidden(string name) =>
        name.StartsWith("Proxy-", StringComparison.OrdinalIgnoreCase)
        || ForbiddenNames.Any(forbidden => SameName(forbidden, name));

    /// <summary>RFC 9110 token: the characters allowed in a header name.</summary>
    private static bool IsToken(string name) => name.Length > 0 && name.All(IsTokenCharacter);

    private static bool IsTokenCharacter(char c) =>
        c is >= 'a' and <= 'z'
        or >= 'A' and <= 'Z'
        or >= '0' and <= '9'
        or '!' or '#' or '$' or '%' or '&' or '\'' or '*' or '+' or '-' or '.' or '^' or '_' or '`' or '|' or '~';

    private static bool IsForbiddenValueCharacter(char c) => (c < 0x20 && c != '\t') || c == 0x7F;
}
