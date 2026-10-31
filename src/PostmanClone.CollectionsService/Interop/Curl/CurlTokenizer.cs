using System.Text;
using PostmanClone.CollectionsService.Contracts;

namespace PostmanClone.CollectionsService.Interop.Curl;

/// <summary>
/// Splits a pasted cURL command into its words the way a POSIX shell quotes them (WP17). This is a tokenizer, never a
/// shell: no variable or command is expanded, nothing runs and nothing is read from disk. Text a shell would expand
/// (<c>$NAME</c>, <c>${...}</c>, <c>$(...)</c>, backticks) is kept as written and reported. An unquoted caret is cmd.exe
/// quoting, which is not supported: it is kept as written and reported.
/// </summary>
public static class CurlTokenizer
{
    public const string UnclosedSingleQuote = "A single quote is never closed.";

    public const string UnclosedDoubleQuote = "A double quote is never closed.";

    public const string UnclosedAnsiQuote = "A $' quote is never closed.";

    public const string NulCharacter = "The command contains a NUL character, which a shell cannot carry.";

    public const string ExpansionCode = "shell_expansion_kept";

    public const string CaretCode = "cmd_syntax_unsupported";

    /// <summary>The words in order, the warnings found while splitting, and the reason the command cannot be split (null when it can).</summary>
    public sealed record Result(IReadOnlyList<string> Words, IReadOnlyList<ImportWarning> Warnings, string? Error);

    public static Result Tokenize(string input)
    {
        if (input.Contains('\0'))
        {
            return Refused(NulCharacter);
        }

        return new Scanner(input).Run();
    }

    private static Result Refused(string error) => new([], [], error);

    /// <summary>One pass over the input. Each method moves <see cref="_at"/> past what it read.</summary>
    private sealed class Scanner(string input)
    {
        private readonly List<string> _words = [];
        private readonly List<ImportWarning> _warnings = [];
        private readonly StringBuilder _word = new();
        private bool _inWord;
        private bool _expansionWarned;
        private bool _caretWarned;
        private int _at;

        public Result Run()
        {
            while (_at < input.Length)
            {
                var character = input[_at];
                if (character == '\\')
                {
                    Backslash();
                }
                else if (character is ' ' or '\t' or '\r' or '\n')
                {
                    EndWord();
                    _at++;
                }
                else if (character == '\'')
                {
                    if (!SingleQuoted())
                    {
                        return Refused(UnclosedSingleQuote);
                    }
                }
                else if (character == '"')
                {
                    if (!DoubleQuoted())
                    {
                        return Refused(UnclosedDoubleQuote);
                    }
                }
                else if (character == '$' && Peek(1) == '\'')
                {
                    if (!AnsiQuoted())
                    {
                        return Refused(UnclosedAnsiQuote);
                    }
                }
                else if (character == '$')
                {
                    NoteDollar();
                    Take(character, 1);
                }
                else if (character == '`')
                {
                    NoteExpansion();
                    Take(character, 1);
                }
                else if (character == '^')
                {
                    NoteCaret();
                    Take(character, 1);
                }
                else
                {
                    Take(character, 1);
                }
            }

            EndWord();
            return _words.Any(word => word.Contains('\0'))
                ? Refused(NulCharacter)
                : new Result(_words, _warnings, null);
        }

        private char Peek(int offset) => _at + offset < input.Length ? input[_at + offset] : '\0';

        /// <summary>Appends a character to the current word and moves past <paramref name="width"/> input characters.</summary>
        private void Take(char character, int width)
        {
            _word.Append(character);
            _inWord = true;
            _at += width;
        }

        private void EndWord()
        {
            if (!_inWord)
            {
                return;
            }

            _words.Add(_word.ToString());
            _word.Clear();
            _inWord = false;
        }

        /// <summary>The length of a line break at <paramref name="position"/> (LF or CRLF), or 0 when there is none.</summary>
        private int LineBreakLength(int position)
        {
            if (position < input.Length && input[position] == '\n')
            {
                return 1;
            }

            return position + 1 < input.Length && input[position] == '\r' && input[position + 1] == '\n' ? 2 : 0;
        }

        /// <summary>An unquoted backslash escapes the next character, or continues the line before a line break.</summary>
        private void Backslash()
        {
            var breakLength = LineBreakLength(_at + 1);
            if (breakLength > 0)
            {
                _at += 1 + breakLength;
                return;
            }

            if (_at + 1 < input.Length)
            {
                Take(input[_at + 1], 2);
            }
            else
            {
                Take('\\', 1);
            }
        }

        private bool SingleQuoted()
        {
            var close = input.IndexOf('\'', _at + 1);
            if (close < 0)
            {
                return false;
            }

            _word.Append(input, _at + 1, close - _at - 1);
            _inWord = true;
            _at = close + 1;
            return true;
        }

        private bool DoubleQuoted()
        {
            _inWord = true;
            _at++;
            while (_at < input.Length)
            {
                var character = input[_at];
                if (character == '"')
                {
                    _at++;
                    return true;
                }

                if (character == '\\' && _at + 1 < input.Length)
                {
                    var escaped = input[_at + 1];
                    if (escaped is '$' or '`' or '"' or '\\')
                    {
                        _word.Append(escaped);
                        _at += 2;
                        continue;
                    }

                    var breakLength = LineBreakLength(_at + 1);
                    if (breakLength > 0)
                    {
                        _at += 1 + breakLength;
                        continue;
                    }
                }

                if (character == '$')
                {
                    NoteDollar();
                }
                else if (character == '`')
                {
                    NoteExpansion();
                }

                _word.Append(character);
                _at++;
            }

            return false;
        }

        /// <summary>A <c>$'...'</c> string: the C escapes are decoded here, and nothing else is.</summary>
        private bool AnsiQuoted()
        {
            _inWord = true;
            _at += 2;
            while (_at < input.Length)
            {
                var character = input[_at];
                if (character == '\'')
                {
                    _at++;
                    return true;
                }

                if (character == '\\' && _at + 1 < input.Length)
                {
                    DecodeEscape();
                    continue;
                }

                _word.Append(character);
                _at++;
            }

            return false;
        }

        /// <summary>Decodes the escape at <see cref="_at"/> (a backslash inside <c>$'...'</c>) and moves past it.</summary>
        private void DecodeEscape()
        {
            var escaped = input[_at + 1];
            var simple = escaped switch
            {
                'a' => '\a',
                'b' => '\b',
                'e' or 'E' => '\u001b',
                'f' => '\f',
                'n' => '\n',
                'r' => '\r',
                't' => '\t',
                'v' => '\v',
                '\\' => '\\',
                '\'' => '\'',
                '"' => '"',
                '?' => '?',
                _ => '\0',
            };

            if (simple != '\0')
            {
                _word.Append(simple);
                _at += 2;
                return;
            }

            if (escaped == 'c' && _at + 2 < input.Length)
            {
                _word.Append((char)(input[_at + 2] & 0x1F));
                _at += 3;
                return;
            }

            if (escaped is 'x' or 'u' or 'U')
            {
                DecodeHex(escaped);
                return;
            }

            if (escaped is >= '0' and <= '7')
            {
                DecodeOctal();
                return;
            }

            // An unknown escape stays as written: the backslash and the character after it.
            _word.Append('\\').Append(escaped);
            _at += 2;
        }

        /// <summary>\xHH (up to two digits), \uHHHH (up to four) and \UHHHHHHHH (up to eight). A value that is no character stays as written.</summary>
        private void DecodeHex(char marker)
        {
            var maxDigits = marker switch
            {
                'x' => 2,
                'u' => 4,
                _ => 8,
            };

            var start = _at + 2;
            var end = start;
            long value = 0;
            while (end < input.Length && end - start < maxDigits && char.IsAsciiHexDigit(input[end]))
            {
                value = (value * 16) + HexDigitValue(input[end]);
                end++;
            }

            if (end == start)
            {
                _word.Append('\\').Append(marker);
                _at += 2;
                return;
            }

            if (marker == 'x')
            {
                _word.Append((char)value);
            }
            else if (value <= 0x10FFFF && !(value >= 0xD800 && value <= 0xDFFF))
            {
                _word.Append(char.ConvertFromUtf32((int)value));
            }
            else
            {
                _word.Append(input, _at, end - _at);
            }

            _at = end;
        }

        /// <summary>\NNN: up to three octal digits, the byte they name.</summary>
        private void DecodeOctal()
        {
            var start = _at + 1;
            var end = start;
            var value = 0;
            while (end < input.Length && end - start < 3 && input[end] is >= '0' and <= '7')
            {
                value = (value * 8) + (input[end] - '0');
                end++;
            }

            _word.Append((char)(value & 0xFF));
            _at = end;
        }

        private static int HexDigitValue(char digit) =>
            digit <= '9' ? digit - '0' : char.ToLowerInvariant(digit) - 'a' + 10;

        /// <summary>A <c>$</c> that a shell would expand: <c>$(</c>, <c>${</c>, <c>$NAME</c>. Reported once per command.</summary>
        private void NoteDollar()
        {
            var next = Peek(1);
            if (next is '(' or '{' or '_' || char.IsAsciiLetter(next))
            {
                NoteExpansion();
            }
        }

        private void NoteExpansion()
        {
            if (_expansionWarned)
            {
                return;
            }

            _expansionWarned = true;
            _warnings.Add(new ImportWarning(
                ExpansionCode,
                "A shell expansion was kept as text and not expanded. Replace it with a {{variable}} if a value is needed.",
                $"command[{_words.Count}]"));
        }

        private void NoteCaret()
        {
            if (_caretWarned)
            {
                return;
            }

            _caretWarned = true;
            _warnings.Add(new ImportWarning(
                CaretCode,
                "Windows cmd.exe caret quoting is not supported; the caret was kept as text.",
                $"command[{_words.Count}]"));
        }
    }
}
