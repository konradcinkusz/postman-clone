using System.Text.RegularExpressions;

namespace PostmanClone.Web.Builder;

/// <summary>A piece of text: plain, or one <c>{{name}}</c> variable reference (CONTRACTS §1).</summary>
public sealed record TextSegment(string Text, string? Variable);

/// <summary>
/// Finds the <c>{{name}}</c> references in a piece of text, so the URL bar can show them as spans (WP19). The syntax is
/// CONTRACTS §1: a name of 1 to 100 characters from <c>[A-Za-z0-9_.-]</c>, no nesting, no expressions. Anything else is plain text.
/// </summary>
public static class VariableTokens
{
    private static readonly Regex Reference = new(@"\{\{([A-Za-z0-9_.\-]{1,100})\}\}", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    /// <summary>The text cut at every reference. Joining the segments' texts gives the input back.</summary>
    public static IReadOnlyList<TextSegment> Segment(string text)
    {
        var segments = new List<TextSegment>();
        var position = 0;
        foreach (Match match in Reference.Matches(text))
        {
            if (match.Index > position)
            {
                segments.Add(new TextSegment(text[position..match.Index], null));
            }

            segments.Add(new TextSegment(match.Value, match.Groups[1].Value));
            position = match.Index + match.Length;
        }

        if (position < text.Length)
        {
            segments.Add(new TextSegment(text[position..], null));
        }

        return segments;
    }

    /// <summary>The distinct variable names in the text, in order of first appearance.</summary>
    public static IReadOnlyList<string> NamesIn(string text) =>
        Segment(text).Where(segment => segment.Variable is not null).Select(segment => segment.Variable!).Distinct(StringComparer.Ordinal).ToList();
}
