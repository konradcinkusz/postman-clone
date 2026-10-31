namespace PostmanClone.Web.Response;

/// <summary>
/// Turns a text body into lines of tokens for the raw view. A line longer than <see cref="JsonPretty.MaxTokenChars"/> is split,
/// so the page never holds one node the size of the whole body. The text is never interpreted.
/// </summary>
public static class TextLines
{
    public static IReadOnlyList<IReadOnlyList<ViewToken>> Of(string text)
    {
        var lines = new List<IReadOnlyList<ViewToken>>();
        var start = 0;
        while (start <= text.Length)
        {
            var end = text.IndexOf('\n', start);
            if (end < 0)
            {
                end = text.Length;
            }

            var line = text.AsSpan(start, end - start);
            if (line.Length > 0 && line[^1] == '\r')
            {
                line = line[..^1];
            }

            lines.Add(Pieces(line.ToString()));
            if (end >= text.Length)
            {
                break;
            }

            start = end + 1;
        }

        return lines;
    }

    private static List<ViewToken> Pieces(string line)
    {
        var tokens = new List<ViewToken>();
        foreach (var token in JsonPretty.Split(new ViewToken(TokenKind.Text, line)))
        {
            tokens.Add(token);
        }

        if (tokens.Count == 0)
        {
            tokens.Add(new ViewToken(TokenKind.Text, string.Empty));
        }

        return tokens;
    }
}
