using System.Globalization;

namespace PostmanClone.CollectionsService.Services;

/// <summary>
/// The optimistic-concurrency header of CONTRACTS §2: a saved request's <c>ETag</c> is its version in quotes, and a
/// write may send <c>If-Match</c> with the version it read. Only the strong form is accepted.
/// </summary>
public static class EntityTags
{
    public static string Format(int version) => $"\"{version.ToString(CultureInfo.InvariantCulture)}\"";

    /// <summary>
    /// The precondition of a write: null when there is none (the header is absent or <c>*</c>), otherwise the version
    /// the client read. Anything else is a bad request, so a typo never silently disables the check.
    /// </summary>
    public static Outcome<int?> ParseIfMatch(string? header)
    {
        if (header is null)
        {
            return Outcome<int?>.Ok(null);
        }

        var value = header.Trim();
        if (value == "*")
        {
            return Outcome<int?>.Ok(null);
        }

        if (value.Length >= 3 && value[0] == '"' && value[^1] == '"'
            && int.TryParse(value[1..^1], NumberStyles.None, CultureInfo.InvariantCulture, out var version)
            && version >= 1)
        {
            return Outcome<int?>.Ok(version);
        }

        return Failures.Invalid("If-Match", "The precondition must be \"*\" or a quoted version such as \"3\".");
    }
}
