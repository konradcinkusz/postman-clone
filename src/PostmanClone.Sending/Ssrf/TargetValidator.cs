using System.Globalization;
using System.Text;
namespace PostmanClone.Sending.Ssrf;

/// <summary>
/// Checks a request URL before anything is sent: the scheme, the syntax, the port, and the host when it is an IP literal.
/// The parsing follows the WHATWG URL rules that browsers and HTTP stacks use, so a URL is read the way the connection
/// will read it: tabs and newlines are dropped, a backslash is a slash, the host is percent-decoded, and every literal
/// spelling of an address is one address. A name is only checked for shape here. Its addresses are checked when the
/// connection is made (<see cref="GuardedConnector"/>), because only then is the address that will be used known.
/// </summary>
public sealed class TargetValidator
{
    private static readonly char[] QueryOrFragment = ['?', '#'];

    private readonly int _urlMaxLength;
    private readonly OutboundRules _rules;

    public TargetValidator(int urlMaxLength, OutboundRules rules)
    {
        _urlMaxLength = urlMaxLength;
        _rules = rules;
    }

    public TargetCheck Validate(string? url)
    {
        if (string.IsNullOrEmpty(url) || url.Length > _urlMaxLength)
        {
            return TargetCheck.Refuse(SsrfErrorCodes.InvalidUrl);
        }

        var text = CleanUrl(url);
        var colon = text.IndexOf(':');
        if (colon <= 0 || !IsSchemeText(text.AsSpan(0, colon)))
        {
            return TargetCheck.Refuse(SsrfErrorCodes.InvalidUrl);
        }

        var scheme = text[..colon].ToLowerInvariant();
        if (scheme is not ("http" or "https"))
        {
            return TargetCheck.Refuse(SsrfErrorCodes.BlockedScheme);
        }

        var rest = text[(colon + 1)..];
        var queryStart = rest.IndexOfAny(QueryOrFragment);
        var head = (queryStart < 0 ? rest : rest[..queryStart]).Replace('\\', '/');
        var tail = queryStart < 0 ? string.Empty : rest[queryStart..];
        var fragmentStart = tail.IndexOf('#');
        if (fragmentStart >= 0)
        {
            tail = tail[..fragmentStart];
        }

        if (!head.StartsWith("//", StringComparison.Ordinal))
        {
            return TargetCheck.Refuse(SsrfErrorCodes.InvalidUrl);
        }

        head = head[2..];
        var pathStart = head.IndexOf('/');
        var authority = pathStart < 0 ? head : head[..pathStart];
        var path = pathStart < 0 ? string.Empty : head[pathStart..];

        // Userinfo is refused outright: "http://allowed.example@127.0.0.1/" reads differently in different parsers.
        if (authority.Contains('@') || !SplitAuthority(authority, out var hostText, out var portText))
        {
            return TargetCheck.Refuse(SsrfErrorCodes.InvalidUrl);
        }

        var defaultPort = scheme == "https" ? 443 : 80;
        if (!TryReadPort(portText, defaultPort, out var port))
        {
            return TargetCheck.Refuse(SsrfErrorCodes.InvalidUrl);
        }

        if (_rules.IsBlockedPort(port))
        {
            return TargetCheck.Refuse(SsrfErrorCodes.BlockedPort);
        }

        var host = Uri.UnescapeDataString(hostText);
        var kind = IpLiteralParser.Classify(host, out var address);
        string canonicalHost;
        bool isLiteral;
        switch (kind)
        {
            case HostKind.Invalid:
                return TargetCheck.Refuse(SsrfErrorCodes.InvalidUrl);

            case HostKind.Address:
                var verdict = _rules.CheckAddress(address!);
                if (!verdict.Allowed)
                {
                    return TargetCheck.Refuse(SsrfErrorCodes.BlockedAddress, verdict.Category);
                }

                canonicalHost = address!.ToString();
                isLiteral = true;
                break;

            default:
                if (!TryNormalizeName(host, out canonicalHost))
                {
                    return TargetCheck.Refuse(SsrfErrorCodes.InvalidUrl);
                }

                // "localhost" is loopback under any spelling. It is refused by name here so the refusal does not
                // depend on the resolver; with AllowLoopback it is resolved and its address checked as usual.
                if (IsLocalhost(canonicalHost) && !_rules.Options.AllowLoopback)
                {
                    return TargetCheck.Refuse(SsrfErrorCodes.BlockedAddress, "loopback");
                }

                isLiteral = false;
                break;
        }

        if (_rules.IsOwnListener(canonicalHost, isLiteral, isLiteral ? address : null, port))
        {
            return TargetCheck.Refuse(SsrfErrorCodes.BlockedPort);
        }

        var hostForUrl = isLiteral && canonicalHost.Contains(':') ? $"[{canonicalHost}]" : canonicalHost;
        var portForUrl = port == defaultPort ? string.Empty : $":{port.ToString(CultureInfo.InvariantCulture)}";
        var pathForUrl = path.Length == 0 ? "/" : path;
        var uri = $"{scheme}://{hostForUrl}{portForUrl}{pathForUrl}{tail}";

        return TargetCheck.Accept(new SsrfTarget(scheme, canonicalHost, port, uri, isLiteral));
    }

    /// <summary>Drops the leading and trailing C0 controls and spaces, and every tab and line break, as the URL standard does.</summary>
    private static string CleanUrl(string url)
    {
        var start = 0;
        var end = url.Length;
        while (start < end && url[start] <= ' ')
        {
            start++;
        }

        while (end > start && url[end - 1] <= ' ')
        {
            end--;
        }

        var builder = new StringBuilder(end - start);
        for (var i = start; i < end; i++)
        {
            if (url[i] is not ('\t' or '\n' or '\r'))
            {
                builder.Append(url[i]);
            }
        }

        return builder.ToString();
    }

    private static bool IsSchemeText(ReadOnlySpan<char> scheme)
    {
        if (!char.IsAsciiLetter(scheme[0]))
        {
            return false;
        }

        foreach (var c in scheme)
        {
            if (!(char.IsAsciiLetterOrDigit(c) || c is '+' or '-' or '.'))
            {
                return false;
            }
        }

        return true;
    }

    private static bool SplitAuthority(string authority, out string host, out string port)
    {
        host = string.Empty;
        port = string.Empty;

        if (authority.StartsWith('['))
        {
            var close = authority.IndexOf(']');
            if (close < 0)
            {
                return false;
            }

            host = authority[..(close + 1)];
            var after = authority[(close + 1)..];
            if (after.Length > 0)
            {
                if (after[0] != ':')
                {
                    return false;
                }

                port = after[1..];
            }
        }
        else
        {
            var colon = authority.IndexOf(':');
            if (colon < 0)
            {
                host = authority;
            }
            else
            {
                host = authority[..colon];
                port = authority[(colon + 1)..];
                if (port.Contains(':'))
                {
                    return false;
                }
            }
        }

        return host.Length > 0;
    }

    private static bool TryReadPort(string text, int defaultPort, out int port)
    {
        port = defaultPort;
        if (text.Length == 0)
        {
            return true;
        }

        if (text.Length > 5 || !text.All(char.IsAsciiDigit))
        {
            return false;
        }

        port = int.Parse(text, CultureInfo.InvariantCulture);
        return port is >= 1 and <= 65535;
    }

    /// <summary>Lower-cases and punycodes a name, drops one trailing dot, and checks the labels. False means invalid.</summary>
    private static bool TryNormalizeName(string host, out string name)
    {
        name = host.ToLowerInvariant();
        if (name.EndsWith('.'))
        {
            name = name[..^1];
        }

        if (name.Any(c => c > 0x7F))
        {
            try
            {
                name = new IdnMapping().GetAscii(name);
            }
            catch (ArgumentException)
            {
                return false;
            }
        }

        if (name.Length is 0 or > 253)
        {
            return false;
        }

        foreach (var label in name.Split('.'))
        {
            if (label.Length is 0 or > 63 || !label.All(IsLabelCharacter))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsLabelCharacter(char c) => char.IsAsciiLetterOrDigit(c) || c is '-' or '_';

    internal static bool IsLocalhost(string name) => name == "localhost" || name.EndsWith(".localhost", StringComparison.Ordinal);
}
