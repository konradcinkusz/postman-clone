using PostmanClone.Web.Response;

namespace PostmanClone.Web.Components.Response;

/// <summary>
/// The response panel's small decisions, kept out of its markup (BLAZOR-WEB-APP §10): which token class a piece of text gets, how
/// many lines show at once, and the labels of the tabs. Pure, so a test reads them without rendering.
/// </summary>
public static class ResponsePresentation
{
    /// <summary>Lines drawn at once. The rest wait behind "show more", so a large body never becomes one large DOM.</summary>
    public const int LinesPerChunk = 400;

    public static string TokenClass(TokenKind kind) => kind switch
    {
        TokenKind.Key => "json-key",
        TokenKind.String => "json-string",
        TokenKind.Number => "json-number",
        TokenKind.Keyword => "json-keyword",
        TokenKind.Punctuation => "json-punct",
        _ => string.Empty,
    };

    public static string ModeLabel(BodyMode mode) => mode switch
    {
        BodyMode.Pretty => "Pretty",
        BodyMode.Raw => "Raw",
        _ => "Preview",
    };

    /// <summary>The lines drawn for a chunk count: at most <see cref="LinesPerChunk"/> per chunk, and never more than there are.</summary>
    public static int LinesShown(int chunks, int total) => Math.Min(total, Math.Max(1, chunks) * LinesPerChunk);

    /// <summary>A variable as the user writes it, with its braces, for the list of unresolved names.</summary>
    public static string VariableText(string name) => "{{" + name + "}}";

    /// <summary>The text of a timing phase: the value, or "not measured" when the sender reported 0 (CONTRACTS §4: unknown is 0).</summary>
    public static string Phase(int milliseconds) => milliseconds == 0 ? "not measured" : ResponseFormat.Milliseconds(milliseconds);
}
