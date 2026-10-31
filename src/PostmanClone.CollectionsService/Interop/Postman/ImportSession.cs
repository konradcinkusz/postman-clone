using System.Globalization;
using PostmanClone.CollectionsService.Contracts;
using PostmanClone.CollectionsService.Domain;

namespace PostmanClone.CollectionsService.Interop.Postman;

/// <summary>
/// The state of one import: the warnings, the counts that the limits bound, the folders and requests found so far, and the
/// names generated for literal secrets. One session is one file; it is discarded when the import ends.
/// </summary>
internal sealed class ImportSession
{
    private readonly List<ImportWarning> _warnings = [];
    private readonly List<FolderDraft> _folders = [];
    private readonly List<RequestDraft> _requests = [];
    private int _items;
    private int _omittedWarnings;
    private int _generatedNames;

    /// <summary>Records a warning. Past <see cref="PostmanImportLimits.MaxWarnings"/> they are counted, not listed.</summary>
    public void Warn(string code, string message, string path)
    {
        if (_warnings.Count < PostmanImportLimits.MaxWarnings)
        {
            _warnings.Add(new ImportWarning(code, message, path));
        }
        else
        {
            _omittedWarnings++;
        }
    }

    /// <summary>Counts one folder or request. The file may not hold more than <see cref="PostmanImportLimits.MaxItems"/> of them.</summary>
    public void CountItem()
    {
        _items++;
        if (_items > PostmanImportLimits.MaxItems)
        {
            throw ImportRefusal.TooLarge($"A collection to import holds at most {PostmanImportLimits.MaxItems} folders and requests.");
        }
    }

    /// <summary>Adds a folder under <paramref name="parentKey"/> and returns its key.</summary>
    public int AddFolder(int? parentKey, string name)
    {
        if (_folders.Count >= CollectionLimits.MaxFoldersPerCollection)
        {
            throw ImportRefusal.TooLarge($"A collection holds at most {CollectionLimits.MaxFoldersPerCollection} folders.");
        }

        var key = _folders.Count;
        _folders.Add(new FolderDraft(key, parentKey, name));
        return key;
    }

    /// <summary>
    /// Adds a request under <paramref name="folderKey"/> after the definition passes the shared validator, the same one the
    /// API uses (CONTRACTS §1). A broken rule refuses the whole import, and the error names the path of the value.
    /// </summary>
    public void AddRequest(int? folderKey, string name, string? description, RequestDefinition definition, string path)
    {
        if (_requests.Count >= CollectionLimits.MaxRequestsPerCollection)
        {
            throw ImportRefusal.TooLarge($"A collection holds at most {CollectionLimits.MaxRequestsPerCollection} requests.");
        }

        var normalized = RequestDefinitionValidator.Normalize(definition);
        if (RequestDefinitionValidator.ExceedsMaxSize(normalized))
        {
            throw ImportRefusal.TooLarge($"A request is larger than {RequestDefinitionLimits.MaxJsonBytes} bytes of JSON.");
        }

        var validation = RequestDefinitionValidator.Validate(normalized);
        if (!validation.IsValid)
        {
            var errors = validation.Issues
                .GroupBy(issue => $"{path}.{issue.Field}")
                .ToDictionary(group => group.Key, group => group.Select(issue => issue.Message).ToArray());
            throw ImportRefusal.FromErrors(errors);
        }

        _requests.Add(new RequestDraft(folderKey, name, description, normalized));
    }

    /// <summary>
    /// A variable name for a literal secret the file held. It is generated, unique in the import, and matches the
    /// <c>{{name}}</c> syntax of CONTRACTS §1. The literal itself is never kept.
    /// </summary>
    public string NewSecretVariable(string kind)
    {
        _generatedNames++;
        return "imported_" + kind + "_" + _generatedNames.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>Trims a name to its limit, or sets a fallback for an empty one. Every change is a warning.</summary>
    public string Name(string? raw, string path, string fallback, int maxLength)
    {
        var text = raw ?? string.Empty;
        var trimmed = text.Trim();
        if (trimmed.Length == 0)
        {
            Warn("name_adjusted", $"The name is missing or blank, so it was set to '{fallback}'.", path);
            return fallback;
        }

        if (trimmed.Length > maxLength)
        {
            Warn("name_adjusted", $"The name is longer than {maxLength} characters and was cut to fit.", path);
            return Cut(trimmed, maxLength);
        }

        if (trimmed.Length != text.Length)
        {
            Warn("name_adjusted", "Spaces at the start or end of the name were removed.", path);
        }

        return trimmed;
    }

    /// <summary>A description, cut to <see cref="PostmanImportLimits.MaxDescriptionLength"/> with a warning when it is longer.</summary>
    public string? Description(string? raw, string path)
    {
        if (raw is null)
        {
            return null;
        }

        if (raw.Length <= PostmanImportLimits.MaxDescriptionLength)
        {
            return raw;
        }

        Warn("text_truncated", $"The description is longer than {PostmanImportLimits.MaxDescriptionLength} characters and was cut.", path);
        return Cut(raw, PostmanImportLimits.MaxDescriptionLength);
    }

    /// <summary>The plan: folders and requests with their sort orders, which are their places among their siblings.</summary>
    public ImportPlan Finish(string name, string? description)
    {
        var folderOrder = new Dictionary<int, int>();
        var folders = _folders.Select(draft => new PlannedFolder(draft.Key, draft.ParentKey, draft.Name, NextOrder(folderOrder, draft.ParentKey))).ToList();

        var requestOrder = new Dictionary<int, int>();
        var requests = _requests.Select(draft => new PlannedRequest(
            draft.FolderKey,
            draft.Name,
            draft.Description,
            draft.Definition,
            NextOrder(requestOrder, draft.FolderKey))).ToList();

        var warnings = _warnings.ToList();
        if (_omittedWarnings > 0)
        {
            warnings.Add(new ImportWarning(
                "warnings_omitted",
                $"{_omittedWarnings.ToString(CultureInfo.InvariantCulture)} more warnings were not listed.",
                "$"));
        }

        return new ImportPlan(name, description, folders, requests, warnings);
    }

    /// <summary>The next sort order among one parent's children. The top of the collection is the key -1.</summary>
    private static int NextOrder(Dictionary<int, int> counters, int? parent)
    {
        var key = parent ?? -1;
        counters.TryGetValue(key, out var next);
        counters[key] = next + 1;
        return next;
    }

    /// <summary>Cuts text to a length without leaving half of a surrogate pair at the end.</summary>
    private static string Cut(string text, int maxLength)
    {
        var length = maxLength;
        if (length > 0 && char.IsHighSurrogate(text[length - 1]))
        {
            length--;
        }

        return text[..length];
    }

    private sealed record FolderDraft(int Key, int? ParentKey, string Name);

    private sealed record RequestDraft(int? FolderKey, string Name, string? Description, RequestDefinition Definition);
}
