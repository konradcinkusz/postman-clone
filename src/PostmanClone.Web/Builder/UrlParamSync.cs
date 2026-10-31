using System.Text;

namespace PostmanClone.Web.Builder;

/// <summary>One query pair as the URL spells it, decoded: a key and a value, both plain text.</summary>
public sealed record UrlPair(string Key, string Value);

/// <summary>A URL taken apart: the text before the query, the decoded query pairs, and the fragment (null when there is no '#').</summary>
public sealed record UrlParts(string Base, IReadOnlyList<UrlPair> Pairs, string? Fragment);

/// <summary>
/// The sync between a request's URL text and its parameter table (WP19, ADR-0017). Pure string logic, no renderer.
/// <para>
/// The URL holds only the enabled rows that have a key. A disabled row, or a row whose key is still empty, lives in the
/// table alone, so editing the URL keeps those rows where they were (<see cref="MergeFromUrl"/>). Encoding follows RFC 3986:
/// an unreserved character, a sub-delimiter other than '&amp;', '=' and '+', and the braces of a <c>{{variable}}</c> stay
/// as written; everything else is UTF-8 percent-encoded. '+' stays a literal plus, both ways, so no value changes meaning
/// on the way through. <see cref="Decode"/> leaves an escape it cannot read (or whose bytes are not UTF-8) as written.
/// </para>
/// </summary>
public static class UrlParamSync
{
    private const string UpperHex = "0123456789ABCDEF";

    /// <summary>Takes a URL apart. The fragment is cut first, so a '?' after the '#' stays part of it.</summary>
    public static UrlParts Split(string url)
    {
        string? fragment = null;
        var rest = url;
        var hash = rest.IndexOf('#', StringComparison.Ordinal);
        if (hash >= 0)
        {
            fragment = rest[(hash + 1)..];
            rest = rest[..hash];
        }

        var question = rest.IndexOf('?', StringComparison.Ordinal);
        return question < 0
            ? new UrlParts(rest, [], fragment)
            : new UrlParts(rest[..question], ParseQuery(rest[(question + 1)..]), fragment);
    }

    /// <summary>The decoded pairs of a query string. Empty segments ("a=1&amp;&amp;b") are skipped; a segment with no '=' has an empty value.</summary>
    public static IReadOnlyList<UrlPair> ParseQuery(string query)
    {
        var pairs = new List<UrlPair>();
        foreach (var segment in query.Split('&'))
        {
            if (segment.Length == 0)
            {
                continue;
            }

            var equals = segment.IndexOf('=', StringComparison.Ordinal);
            var key = equals < 0 ? segment : segment[..equals];
            var value = equals < 0 ? string.Empty : segment[(equals + 1)..];
            pairs.Add(new UrlPair(Decode(key), Decode(value)));
        }

        return pairs;
    }

    /// <summary>
    /// Rebuilds a URL from its base, its pairs and its fragment. A pair with an empty key is left out: an incomplete row
    /// must not write <c>?=</c> into the URL the user is editing.
    /// </summary>
    public static string Join(string @base, IEnumerable<UrlPair> pairs, string? fragment)
    {
        var builder = new StringBuilder(@base);
        var first = true;
        foreach (var pair in pairs)
        {
            if (pair.Key.Length == 0)
            {
                continue;
            }

            builder.Append(first ? '?' : '&');
            first = false;
            builder.Append(Encode(pair.Key)).Append('=').Append(Encode(pair.Value));
        }

        if (fragment is not null)
        {
            builder.Append('#').Append(fragment);
        }

        return builder.ToString();
    }

    /// <summary>Writes the URL for a table edit: the base and the fragment stay, and the query becomes the given pairs.</summary>
    public static string Compose(string url, IEnumerable<UrlPair> pairs)
    {
        var parts = Split(url);
        return Join(parts.Base, pairs, parts.Fragment);
    }

    /// <summary>
    /// The table after a URL edit. The rows the URL can show (enabled, with a key) take the parsed pairs in order, reusing the
    /// id of the row at the same place. Every other row keeps its index, as far as the list allows.
    /// </summary>
    public static IReadOnlyList<DraftRow> MergeFromUrl(IReadOnlyList<DraftRow> previous, IReadOnlyList<UrlPair> parsed, Func<Guid> newId)
    {
        var previousInUrl = previous.Where(IsInUrl).ToList();
        var result = new List<DraftRow>(parsed.Count + previous.Count);
        for (var index = 0; index < parsed.Count; index++)
        {
            var id = index < previousInUrl.Count ? previousInUrl[index].Id : newId();
            result.Add(new DraftRow(id, parsed[index].Key, parsed[index].Value, Enabled: true));
        }

        for (var index = 0; index < previous.Count; index++)
        {
            if (!IsInUrl(previous[index]))
            {
                result.Insert(Math.Min(index, result.Count), previous[index]);
            }
        }

        return result;
    }

    /// <summary>The pairs a table writes into the URL: enabled rows, in order, with a key.</summary>
    public static IReadOnlyList<UrlPair> PairsOf(IEnumerable<DraftRow> rows) =>
        rows.Where(IsInUrl).Select(row => new UrlPair(row.Key, row.Value)).ToList();

    /// <summary>True when a row is written into the URL: enabled, and with a key to write.</summary>
    public static bool IsInUrl(DraftRow row) => row.Enabled && row.Key.Length > 0;

    /// <summary>
    /// Percent-encodes text for a query key or value. Unreserved characters, the sub-delimiters other than '&amp;', '=' and '+',
    /// ':', '@', '/', '?' and the braces of a variable pass as written. Every other character is encoded as UTF-8 bytes.
    /// </summary>
    public static string Encode(string text)
    {
        var builder = new StringBuilder(text.Length);
        foreach (var b in Encoding.UTF8.GetBytes(text))
        {
            if (IsPassThrough(b))
            {
                builder.Append((char)b);
            }
            else
            {
                builder.Append('%').Append(UpperHex[b >> 4]).Append(UpperHex[b & 0xF]);
            }
        }

        return builder.ToString();
    }

    /// <summary>
    /// Reverses <see cref="Encode"/> and reads any other escape the same way. A run of escapes that is not valid UTF-8 is
    /// kept exactly as written, so a stray '%' in a URL the user typed is not silently rewritten.
    /// </summary>
    public static string Decode(string text)
    {
        if (text.IndexOf('%', StringComparison.Ordinal) < 0)
        {
            return text;
        }

        var builder = new StringBuilder(text.Length);
        var index = 0;
        while (index < text.Length)
        {
            if (!IsEscapeAt(text, index))
            {
                builder.Append(text[index]);
                index++;
                continue;
            }

            var bytes = new List<byte>();
            var end = index;
            while (IsEscapeAt(text, end))
            {
                bytes.Add(ParseHex(text[end + 1], text[end + 2]));
                end += 3;
            }

            if (TryUtf8(bytes, out var decoded))
            {
                builder.Append(decoded);
            }
            else
            {
                builder.Append(text, index, end - index);
            }

            index = end;
        }

        return builder.ToString();
    }

    private static bool IsPassThrough(byte b) =>
        b is (>= (byte)'a' and <= (byte)'z') or (>= (byte)'A' and <= (byte)'Z') or (>= (byte)'0' and <= (byte)'9')
            || b is (byte)'-' or (byte)'.' or (byte)'_' or (byte)'~'
            || b is (byte)'!' or (byte)'$' or (byte)'\'' or (byte)'(' or (byte)')' or (byte)'*' or (byte)'+' or (byte)','
            || b is (byte)';' or (byte)':' or (byte)'@' or (byte)'/' or (byte)'?'
            || b is (byte)'{' or (byte)'}';

    private static bool IsEscapeAt(string text, int index) =>
        index + 2 < text.Length
        && text[index] == '%'
        && IsHex(text[index + 1])
        && IsHex(text[index + 2]);

    private static bool IsHex(char c) => c is (>= '0' and <= '9') or (>= 'a' and <= 'f') or (>= 'A' and <= 'F');

    private static byte ParseHex(char high, char low) => (byte)((HexValue(high) << 4) | HexValue(low));

    private static int HexValue(char c) => c switch
    {
        >= '0' and <= '9' => c - '0',
        >= 'a' and <= 'f' => c - 'a' + 10,
        _ => c - 'A' + 10,
    };

    private static bool TryUtf8(List<byte> bytes, out string text)
    {
        try
        {
            text = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true).GetString(bytes.ToArray());
            return true;
        }
        catch (DecoderFallbackException)
        {
            text = string.Empty;
            return false;
        }
    }
}
