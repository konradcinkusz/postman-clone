using System.Text;
using PostmanClone.ExecutionService.Api;

namespace PostmanClone.ExecutionService.History;

/// <summary>
/// Cuts the response body kept in a history entry to <c>HistoryBodyMaxBytes</c> (CONTRACTS §4, ADR-0015). The cut is
/// made on the masked text, so it never reveals more than the masked form. Text is cut at a character boundary, so the
/// stored body stays valid UTF-8. Base64 is cut at a four-character group, so it stays decodable. The caller's own
/// answer is not affected: only the stored copy is cut, and it is marked <c>truncated</c>.
/// </summary>
public static class HistoryBodyLimit
{
    private const string Base64 = "base64";

    public static ExecutionResult Apply(ExecutionResult result, int maxBytes)
    {
        var response = result.Response;
        if (response is null || Encoding.UTF8.GetByteCount(response.Body.Content) <= maxBytes)
        {
            return result;
        }

        var body = response.Body;
        var content = body.Encoding == Base64 ? CutBase64(body.Content, maxBytes) : CutText(body.Content, maxBytes);
        return result with { Response = response with { Body = body with { Content = content, Truncated = true } } };
    }

    /// <summary>The longest prefix whose UTF-8 form is at most <paramref name="maxBytes"/> bytes, cut between two code points.</summary>
    public static string CutText(string text, int maxBytes)
    {
        var used = 0;
        var end = 0;
        foreach (var rune in text.EnumerateRunes())
        {
            if (used + rune.Utf8SequenceLength > maxBytes)
            {
                break;
            }

            used += rune.Utf8SequenceLength;
            end += rune.Utf16SequenceLength;
        }

        return text[..end];
    }

    /// <summary>Base64 is ASCII, so one character is one byte; a cut is made on a four-character group.</summary>
    public static string CutBase64(string text, int maxBytes) => text[..Math.Min(text.Length, maxBytes / 4 * 4)];
}
