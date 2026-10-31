using System.Text;

namespace PostmanClone.CollectionsService.Interop.Curl;

/// <summary>
/// The text forms the cURL interop needs: a POSIX single-quoted word, a form-encoded component and a query string
/// appended to a URL (WP17). Quoting is what keeps an exported value inert: inside single quotes a shell reads every
/// byte literally, and the only character that needs care is the single quote itself.
/// </summary>
public static class CurlQuoting
{
    /// <summary>
    /// Wraps the value in single quotes. A quote inside it becomes <c>'\''</c> (close, escaped quote, reopen). A NUL cannot
    /// be carried by a shell, so it becomes U+FFFD.
    /// </summary>
    public static string Quote(string value) =>
        "'" + value.Replace('\0', '�').Replace("'", "'\\''", StringComparison.Ordinal) + "'";

    /// <summary>
    /// Percent-encodes every UTF-8 byte except the RFC 3986 unreserved characters. Braces are kept, so a
    /// <c>{{variable}}</c> reference survives an export as written (CONTRACTS §2).
    /// </summary>
    public static string Escape(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var octet in Encoding.UTF8.GetBytes(value))
        {
            var character = (char)octet;
            if (octet < 0x80 && (char.IsAsciiLetterOrDigit(character) || character is '-' or '.' or '_' or '~' or '{' or '}'))
            {
                builder.Append(character);
            }
            else
            {
                builder.Append('%').Append(octet.ToString("X2", System.Globalization.CultureInfo.InvariantCulture));
            }
        }

        return builder.ToString();
    }

    /// <summary>Appends a query string to a URL, before any fragment, with <c>?</c> or <c>&amp;</c> as the URL needs.</summary>
    public static string AppendQuery(string url, string query)
    {
        if (query.Length == 0)
        {
            return url;
        }

        var fragmentAt = url.IndexOf('#');
        var core = fragmentAt < 0 ? url : url[..fragmentAt];
        var fragment = fragmentAt < 0 ? string.Empty : url[fragmentAt..];
        var separator = !core.Contains('?') ? "?" : core.EndsWith('?') || core.EndsWith('&') ? string.Empty : "&";
        return core + separator + query + fragment;
    }
}
