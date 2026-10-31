namespace PostmanClone.Web.Response;

/// <summary>The kind of a piece of text on the page. Each kind has a token colour; none is markup (ADR-0022).</summary>
public enum TokenKind
{
    Text,
    Whitespace,
    Punctuation,
    Key,
    String,
    Number,
    Keyword,
}

/// <summary>One piece of text and its kind. The text is data from the response: the page encodes it, and it is never parsed as markup.</summary>
public readonly record struct ViewToken(TokenKind Kind, string Text);

/// <summary>
/// Pretty-prints JSON text as lines of tokens (BLAZOR-WEB-APP §10: plain logic, tested without a browser). The scanner is
/// iterative, with an explicit stack, so a body nested ten thousand levels deep is read without a stack overflow. Text that is
/// not valid JSON is not formatted: <see cref="TryFormat"/> returns null and the caller shows the raw text.
/// </summary>
public static class JsonPretty
{
    /// <summary>The indentation stops growing past this depth, so a deep body cannot make every line wider than the page.</summary>
    public const int MaxIndentLevels = 64;

    /// <summary>The longest piece of text one token carries; a longer string or key is split into tokens of this size.</summary>
    public const int MaxTokenChars = 4096;

    private enum State
    {
        Value,
        ArrayFirst,
        ObjectFirst,
        Key,
        Colon,
        AfterValue,
    }

    /// <summary>The lines of the formatted text, or null when the text is not one JSON value.</summary>
    public static IReadOnlyList<IReadOnlyList<ViewToken>>? TryFormat(string text)
    {
        var tokens = Scan(text);
        return tokens is null ? null : Layout(tokens);
    }

    private static List<ViewToken>? Scan(string text)
    {
        var tokens = new List<ViewToken>();
        var stack = new Stack<char>();
        var state = State.Value;
        var i = 0;
        var n = text.Length;

        while (true)
        {
            while (i < n && IsWhitespace(text[i]))
            {
                i++;
            }

            if (i >= n)
            {
                break;
            }

            var c = text[i];
            switch (state)
            {
                case State.Value:
                    if (c == '{')
                    {
                        tokens.Add(new ViewToken(TokenKind.Punctuation, "{"));
                        stack.Push('{');
                        state = State.ObjectFirst;
                        i++;
                    }
                    else if (c == '[')
                    {
                        tokens.Add(new ViewToken(TokenKind.Punctuation, "["));
                        stack.Push('[');
                        state = State.ArrayFirst;
                        i++;
                    }
                    else if (c == '"')
                    {
                        var end = ScanString(text, i);
                        if (end < 0)
                        {
                            return null;
                        }

                        tokens.Add(new ViewToken(TokenKind.String, text[i..end]));
                        state = State.AfterValue;
                        i = end;
                    }
                    else if (c == '-' || c is >= '0' and <= '9')
                    {
                        var end = ScanNumber(text, i);
                        if (end < 0)
                        {
                            return null;
                        }

                        tokens.Add(new ViewToken(TokenKind.Number, text[i..end]));
                        state = State.AfterValue;
                        i = end;
                    }
                    else if (TryLiteral(text, i, out var literal))
                    {
                        tokens.Add(new ViewToken(TokenKind.Keyword, literal));
                        state = State.AfterValue;
                        i += literal.Length;
                    }
                    else
                    {
                        return null;
                    }

                    break;

                case State.ArrayFirst:
                    if (c == ']')
                    {
                        tokens.Add(new ViewToken(TokenKind.Punctuation, "]"));
                        stack.Pop();
                        state = State.AfterValue;
                        i++;
                    }
                    else
                    {
                        state = State.Value;
                    }

                    break;

                case State.ObjectFirst:
                    if (c == '}')
                    {
                        tokens.Add(new ViewToken(TokenKind.Punctuation, "}"));
                        stack.Pop();
                        state = State.AfterValue;
                        i++;
                    }
                    else if (c == '"')
                    {
                        state = State.Key;
                    }
                    else
                    {
                        return null;
                    }

                    break;

                case State.Key:
                {
                    if (c != '"')
                    {
                        return null;
                    }

                    var end = ScanString(text, i);
                    if (end < 0)
                    {
                        return null;
                    }

                    tokens.Add(new ViewToken(TokenKind.Key, text[i..end]));
                    state = State.Colon;
                    i = end;
                    break;
                }

                case State.Colon:
                    if (c != ':')
                    {
                        return null;
                    }

                    tokens.Add(new ViewToken(TokenKind.Punctuation, ":"));
                    state = State.Value;
                    i++;
                    break;

                case State.AfterValue:
                    if (stack.Count == 0)
                    {
                        // A second value after a complete one: the text is not a single JSON value.
                        return null;
                    }

                    if (c == ',')
                    {
                        tokens.Add(new ViewToken(TokenKind.Punctuation, ","));
                        state = stack.Peek() == '{' ? State.Key : State.Value;
                        i++;
                    }
                    else if ((c == '}' && stack.Peek() == '{') || (c == ']' && stack.Peek() == '['))
                    {
                        tokens.Add(new ViewToken(TokenKind.Punctuation, c.ToString()));
                        stack.Pop();
                        i++;
                    }
                    else
                    {
                        return null;
                    }

                    break;
            }
        }

        return stack.Count == 0 && state == State.AfterValue && tokens.Count > 0 ? tokens : null;
    }

    private static IReadOnlyList<IReadOnlyList<ViewToken>> Layout(List<ViewToken> tokens)
    {
        var lines = new List<IReadOnlyList<ViewToken>>();
        var line = new List<ViewToken>();
        var depth = 0;
        var hasContent = false;

        void Add(ViewToken token)
        {
            foreach (var piece in Split(token))
            {
                line.Add(piece);
            }

            hasContent = true;
        }

        void Break()
        {
            if (hasContent)
            {
                lines.Add(line);
            }

            line = [];
            hasContent = false;
            if (depth > 0)
            {
                line.Add(new ViewToken(TokenKind.Whitespace, new string(' ', Math.Min(depth, MaxIndentLevels) * 2)));
            }
        }

        for (var k = 0; k < tokens.Count; k++)
        {
            var token = tokens[k];
            if (IsPunctuation(token, "{") || IsPunctuation(token, "["))
            {
                var close = token.Text == "{" ? "}" : "]";
                if (k + 1 < tokens.Count && IsPunctuation(tokens[k + 1], close))
                {
                    // An empty object or array stays on one line.
                    Add(new ViewToken(TokenKind.Punctuation, token.Text + close));
                    k++;
                    continue;
                }

                Add(token);
                depth++;
                Break();
            }
            else if (IsPunctuation(token, "}") || IsPunctuation(token, "]"))
            {
                depth--;
                Break();
                Add(token);
            }
            else if (IsPunctuation(token, ","))
            {
                Add(token);
                Break();
            }
            else if (IsPunctuation(token, ":"))
            {
                Add(new ViewToken(TokenKind.Punctuation, ": "));
            }
            else
            {
                Add(token);
            }
        }

        if (hasContent)
        {
            lines.Add(line);
        }

        return lines;
    }

    /// <summary>Splits one long token into pieces of at most <see cref="MaxTokenChars"/>, so no single node is huge.</summary>
    public static IEnumerable<ViewToken> Split(ViewToken token)
    {
        if (token.Text.Length <= MaxTokenChars)
        {
            yield return token;
            yield break;
        }

        for (var offset = 0; offset < token.Text.Length; offset += MaxTokenChars)
        {
            yield return token with { Text = token.Text.Substring(offset, Math.Min(MaxTokenChars, token.Text.Length - offset)) };
        }
    }

    private static bool IsPunctuation(ViewToken token, string text) => token.Kind == TokenKind.Punctuation && token.Text == text;

    private static bool IsWhitespace(char c) => c is ' ' or '\t' or '\n' or '\r';

    private static bool TryLiteral(string text, int at, out string literal)
    {
        foreach (var candidate in new[] { "true", "false", "null" })
        {
            if (text.AsSpan(at).StartsWith(candidate, StringComparison.Ordinal))
            {
                literal = candidate;
                return true;
            }
        }

        literal = string.Empty;
        return false;
    }

    /// <summary>The index after a string that starts at <paramref name="start"/>, or -1 when it is not a valid JSON string.</summary>
    private static int ScanString(string text, int start)
    {
        var j = start + 1;
        while (j < text.Length)
        {
            var c = text[j];
            if (c == '"')
            {
                return j + 1;
            }

            if (c == '\\')
            {
                if (j + 1 >= text.Length)
                {
                    return -1;
                }

                var escape = text[j + 1];
                if (escape == 'u')
                {
                    if (j + 6 > text.Length || !IsHex(text, j + 2, 4))
                    {
                        return -1;
                    }

                    j += 6;
                }
                else if ("\"\\/bfnrt".Contains(escape, StringComparison.Ordinal))
                {
                    j += 2;
                }
                else
                {
                    return -1;
                }
            }
            else if (c < 0x20)
            {
                return -1;
            }
            else
            {
                j++;
            }
        }

        return -1;
    }

    private static bool IsHex(string text, int start, int count)
    {
        for (var k = start; k < start + count; k++)
        {
            if (!Uri.IsHexDigit(text[k]))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>The index after a number that starts at <paramref name="start"/>, or -1. The JSON number grammar, exactly.</summary>
    private static int ScanNumber(string text, int start)
    {
        var j = start;
        if (j < text.Length && text[j] == '-')
        {
            j++;
        }

        if (j >= text.Length || !char.IsAsciiDigit(text[j]))
        {
            return -1;
        }

        if (text[j] == '0')
        {
            j++;
        }
        else
        {
            while (j < text.Length && char.IsAsciiDigit(text[j]))
            {
                j++;
            }
        }

        if (j < text.Length && text[j] == '.')
        {
            j++;
            if (j >= text.Length || !char.IsAsciiDigit(text[j]))
            {
                return -1;
            }

            while (j < text.Length && char.IsAsciiDigit(text[j]))
            {
                j++;
            }
        }

        if (j < text.Length && text[j] is 'e' or 'E')
        {
            j++;
            if (j < text.Length && text[j] is '+' or '-')
            {
                j++;
            }

            if (j >= text.Length || !char.IsAsciiDigit(text[j]))
            {
                return -1;
            }

            while (j < text.Length && char.IsAsciiDigit(text[j]))
            {
                j++;
            }
        }

        return j;
    }
}
