namespace PostmanClone.Web.Builder;

/// <summary>
/// The bulk-edit text of a key/value list (WP19): one row per line, <c>key: value</c>. A disabled row is written with a leading
/// <c>//</c>, the way Postman writes it. A name with a colon cannot be written this way (the first colon splits), and the
/// editor says so. <see cref="Format"/> and <see cref="Parse"/> are inverses for every row without a colon in its key, and
/// without a line break or edge spaces in its key or value.
/// </summary>
public static class BulkRows
{
    private const string DisabledPrefix = "//";

    public static string Format(IEnumerable<DraftRow> rows) =>
        string.Join('\n', rows.Select(row =>
        {
            var prefix = row.Enabled ? string.Empty : DisabledPrefix;
            return row.Value.Length == 0 ? $"{prefix}{row.Key}:" : $"{prefix}{row.Key}: {row.Value}";
        }));

    /// <summary>
    /// The rows of the text. A blank line is skipped. A line without a colon is a name with an empty value. Each row gets a new
    /// id from <paramref name="newId"/>, so the caller replaces the list as a whole.
    /// </summary>
    public static IReadOnlyList<DraftRow> Parse(string text, Func<Guid> newId)
    {
        var rows = new List<DraftRow>();
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            if (line.Trim().Length == 0)
            {
                continue;
            }

            var enabled = !line.TrimStart().StartsWith(DisabledPrefix, StringComparison.Ordinal);
            var body = enabled ? line.Trim() : line.TrimStart()[DisabledPrefix.Length..].Trim();
            var colon = body.IndexOf(':', StringComparison.Ordinal);
            var key = colon < 0 ? body : body[..colon].Trim();
            var value = colon < 0 ? string.Empty : body[(colon + 1)..].Trim();
            rows.Add(new DraftRow(newId(), key, value, enabled));
        }

        return rows;
    }
}
