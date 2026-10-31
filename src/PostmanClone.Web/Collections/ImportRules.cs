using System.Globalization;
using PostmanClone.Web.Api;

namespace PostmanClone.Web.Collections;

/// <summary>Which import the page is running. The two differ in their limits, their target and their messages.</summary>
public enum ImportKind
{
    /// <summary>A Postman v2.1 file, imported into a new collection.</summary>
    Postman,

    /// <summary>A cURL command, imported as a request into a chosen collection or folder.</summary>
    Curl,
}

/// <summary>
/// The Web's checks on an import, before anything is sent (WP23). The file and the command are untrusted input: these checks
/// only keep the page from sending what the service would refuse, and the service stays the boundary (ADR-0016). Each rule is
/// the one CONTRACTS §2 and ADR-0016 state, and a test pins the boundary values.
/// </summary>
public static class ImportRules
{
    /// <summary>The largest Postman file the service reads (ADR-0016: 5 MB).</summary>
    public const long MaximumFileBytes = 5 * 1024 * 1024;

    /// <summary>The longest cURL command (CONTRACTS §2: <c>command(1..20000)</c>).</summary>
    public const int MaximumCommandLength = 20000;

    /// <summary>The longest file name sent with an upload. Longer names are cut; a name is a label, not data.</summary>
    public const int MaximumFileNameLength = 255;

    /// <summary>
    /// The file's answer to the page's checks, or null when it may be sent. The extension and the content type are both hints
    /// from the browser, so either one is enough to accept the file; the service still reads the bytes as JSON.
    /// </summary>
    public static string? FileProblem(string? fileName, long size, string? contentType)
    {
        if (size == 0)
        {
            return "The file is empty. Choose a Postman collection file.";
        }

        if (size > MaximumFileBytes)
        {
            return "The file is larger than 5 MB, so it was not sent. Export a smaller collection, or split it.";
        }

        var isJson = Path.GetExtension(fileName ?? string.Empty).Equals(".json", StringComparison.OrdinalIgnoreCase)
            || string.Equals(contentType, "application/json", StringComparison.OrdinalIgnoreCase);
        return isJson ? null : "Choose a Postman collection file with the .json extension.";
    }

    /// <summary>The command's answer to the page's checks, or null when it may be sent.</summary>
    public static string? CommandProblem(string? command)
    {
        if (string.IsNullOrWhiteSpace(command))
        {
            return "Paste a cURL command to import.";
        }

        return command.Length > MaximumCommandLength
            ? $"The command is longer than {MaximumCommandLength.ToString(CultureInfo.InvariantCulture)} characters, so it was not sent."
            : null;
    }

    /// <summary>
    /// A file name fit to send as a multipart name: the folder part removed, control characters and quotes dropped, and the
    /// length capped. The name only labels the part; nothing on the server reads it as a path.
    /// </summary>
    public static string SafeFileName(string? name)
    {
        var leaf = Path.GetFileName(name ?? string.Empty);
        var kept = new string(leaf.Where(character => !char.IsControl(character) && character is not ('"' or '\\')).ToArray());
        if (kept.Length > MaximumFileNameLength)
        {
            kept = kept[..MaximumFileNameLength];
        }

        return kept.Length == 0 ? "collection.json" : kept;
    }

    /// <summary>What the page says after an import that the service answered with a body: the counts, in plain words.</summary>
    public static string Summary(ImportKind kind, ImportResult result)
    {
        var requests = Plural(result.RequestsCreated, "request", "requests");
        if (kind == ImportKind.Curl)
        {
            return $"Added {requests} to \"{result.CollectionName}\".";
        }

        var folders = Plural(result.FoldersCreated, "folder", "folders");
        return $"Created \"{result.CollectionName}\" with {folders} and {requests}.";
    }

    private static string Plural(int count, string singular, string plural) =>
        $"{count.ToString(CultureInfo.InvariantCulture)} {(count == 1 ? singular : plural)}";
}

/// <summary>One warning code as the page shows it: a title and what it means, in plain words.</summary>
public sealed record WarningDescription(string Title, string Explanation);

/// <summary>One item of a warning group: the message the service wrote and the path of the item in the source.</summary>
public sealed record WarningItem(string Message, string Path);

/// <summary>All warnings with one code, in the order the service returned them.</summary>
public sealed record WarningGroup(string Code, WarningDescription Description, IReadOnlyList<WarningItem> Items);

/// <summary>
/// Groups an import's warnings by code (WP23: "all warnings grouped by code"). The groups come in a fixed order, so the page reads
/// the same for every import; an unknown code still gets a group, with a generic explanation. Nothing here renders markup: the
/// messages are the service's text, and the page writes them as text.
/// </summary>
public static class ImportWarnings
{
    /// <summary>The order of the known codes on the page: what the user must act on comes first.</summary>
    private static readonly string[] KnownOrder =
    [
        "secret_not_imported",
        "sensitive_value_kept",
        "unsupported_auth",
        "unsupported_body",
        "unsupported_option",
        "option_ignored",
        "file_not_imported",
        "scripts_ignored",
        "variables_ignored",
        "invalid_argument",
        "extra_argument_ignored",
        "cmd_syntax_unsupported",
        "name_adjusted",
        "text_truncated",
        "depth_flattened",
        "row_skipped",
        "item_skipped",
        "warnings_omitted",
    ];

    /// <summary>The code whose group names the variables to create. The page adds the link to the environments page there.</summary>
    public const string SecretCode = "secret_not_imported";

    public static IReadOnlyList<WarningGroup> Group(IEnumerable<ImportWarning> warnings)
    {
        var groups = warnings
            .GroupBy(warning => warning.Code, StringComparer.Ordinal)
            .Select(group => new WarningGroup(
                group.Key,
                Describe(group.Key),
                group.Select(warning => new WarningItem(warning.Message, warning.Path)).ToList()))
            .ToList();

        return groups
            .OrderBy(group => Array.IndexOf(KnownOrder, group.Code) is var index && index >= 0 ? index : int.MaxValue)
            .ThenBy(group => group.Code, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>The title and explanation of a code. Titles name the kind of thing, never a value from the file.</summary>
    public static WarningDescription Describe(string code) => code switch
    {
        "secret_not_imported" => new(
            "Secrets were not imported",
            "Each literal secret was replaced by a variable reference, and its value was not kept. Create the variables named below in an environment before you send these requests."),
        "sensitive_value_kept" => new(
            "Sensitive values were kept as written",
            "These values already refer to a variable, so they were kept. Check that the variable exists."),
        "unsupported_auth" => new(
            "Some auth settings were not imported",
            "Only no auth, bearer, basic and API key auth are supported. The request was saved without auth."),
        "unsupported_body" => new(
            "Some bodies were not imported",
            "Only raw, URL-encoded and text form bodies are supported. The request was saved without that body."),
        "unsupported_option" => new(
            "Some cURL options are not supported",
            "The option was left out and the rest of the command was imported."),
        "option_ignored" => new(
            "Some cURL options were ignored",
            "The option has no effect in this app, so it was not kept."),
        "file_not_imported" => new(
            "Files were not imported",
            "File uploads and file references are not supported."),
        "scripts_ignored" => new(
            "Scripts were not imported",
            "Scripts are never run here, so they were left out."),
        "variables_ignored" => new(
            "Collection variables were not imported",
            "Set the values as environment variables instead."),
        "invalid_argument" => new(
            "Some arguments were not understood",
            "They were left out."),
        "extra_argument_ignored" => new(
            "Extra arguments were ignored",
            "Only one URL is imported from a command."),
        "cmd_syntax_unsupported" => new(
            "Windows command syntax is not supported",
            "Paste the command as a POSIX shell command, with no carets."),
        "name_adjusted" => new(
            "Some names were changed",
            "A name was trimmed or shortened to its limit, or given a default because it was empty."),
        "text_truncated" => new(
            "Some text was cut to its limit",
            "A description or a value was longer than its limit and was shortened."),
        "depth_flattened" => new(
            "Some folders were flattened",
            "Folders nested deeper than eight levels were placed in the eighth level."),
        "row_skipped" => new(
            "Some rows were skipped",
            "Rows that could not be imported were left out."),
        "item_skipped" => new(
            "Some items were skipped",
            "Items that could not be imported were left out."),
        "warnings_omitted" => new(
            "More warnings were not listed",
            "The import reported more warnings than the page lists. The count is in the message."),
        _ => new(
            "Other warnings",
            "The importer reported these. Check the items below."),
    };
}

/// <summary>What the page says when an import is refused, by the service's outcome and problem code (CONTRACTS §0, §2).</summary>
public static class ImportMessages
{
    public static string For(ImportKind kind, CollectionOutcome outcome, string? code) => (kind, outcome, code) switch
    {
        (ImportKind.Postman, CollectionOutcome.Invalid, _) =>
            "Nothing was imported: the file is not a valid Postman v2.1 collection, or it breaks one of the import limits.",
        (ImportKind.Curl, CollectionOutcome.Invalid, _) =>
            "Nothing was imported: this does not read as a cURL command the app can import.",
        (_, CollectionOutcome.TooLarge, _) when kind == ImportKind.Postman =>
            "Nothing was imported: the file is larger than 5 MB or holds more than the limits allow (5000 requests, 500 folders).",
        (_, CollectionOutcome.TooLarge, _) =>
            "Nothing was imported: the command is too large to import.",
        (_, CollectionOutcome.Conflict, _) =>
            "Nothing was imported: this workspace already holds the most collections it can (200). Delete one, then import again.",
        (_, CollectionOutcome.NotFound, _) =>
            "Nothing was imported: the collection or folder you chose no longer exists. Choose another and try again.",
        (_, CollectionOutcome.Forbidden, _) =>
            "Nothing was imported: you do not have permission to import into this workspace.",
        (_, CollectionOutcome.Refused, _) =>
            "Nothing was imported: the collections service refused the import.",
        _ => CollectionMessages.For(outcome, code),
    };
}
