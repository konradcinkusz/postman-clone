namespace PostmanClone.CollectionsService.Services;

/// <summary>
/// Makes user text literal inside a SQL <c>LIKE</c> pattern. A <c>%</c> or <c>_</c> typed by the user matches itself, not
/// any run or any character (CONTRACTS §2 search). The escape character is a backslash.
/// </summary>
public static class LikePattern
{
    public const string EscapeCharacter = "\\";

    public static string Escape(string text) => text
        .Replace(EscapeCharacter, EscapeCharacter + EscapeCharacter, StringComparison.Ordinal)
        .Replace("%", EscapeCharacter + "%", StringComparison.Ordinal)
        .Replace("_", EscapeCharacter + "_", StringComparison.Ordinal);
}
