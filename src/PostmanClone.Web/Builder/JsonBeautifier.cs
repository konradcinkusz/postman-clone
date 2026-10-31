using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;

namespace PostmanClone.Web.Builder;

/// <summary>
/// The JSON "Beautify" of the body editor (WP19), with <see cref="System.Text.Json"/> only. A body that is not JSON is
/// refused with the line and column, never with the text itself. Non-ASCII text is written as typed, not escaped.
/// </summary>
public static class JsonBeautifier
{
    /// <summary>The result of a beautify: the new text when it worked, or the message when it did not.</summary>
    public sealed record Result(bool Succeeded, string Text, string? Message);

    private static readonly JsonSerializerOptions Writing = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.Create(UnicodeRanges.All),
    };

    public static Result Beautify(string text)
    {
        if (text.Trim().Length == 0)
        {
            return new Result(false, text, BuilderMessages.EmptyBody);
        }

        try
        {
            using var document = JsonDocument.Parse(text, new JsonDocumentOptions { MaxDepth = 64, CommentHandling = JsonCommentHandling.Disallow });
            return new Result(true, JsonSerializer.Serialize(document.RootElement, Writing), null);
        }
        catch (JsonException exception)
        {
            var line = (exception.LineNumber ?? 0) + 1;
            var column = (exception.BytePositionInLine ?? 0) + 1;
            return new Result(false, text, $"The body is not valid JSON, at line {line}, column {column}.");
        }
    }
}
