using PostmanClone.Web.Api;

namespace PostmanClone.Web.Builder;

/// <summary>
/// The builder's state for one open request (WP19, ADR-0017): the saved name and version, the draft, and whether the draft
/// differs from what was saved. It holds no token and no renderer, so the save rules are tested as plain C#.
/// The saved baseline is a fingerprint of the canonical definition, so "dirty" means "would send something different".
/// </summary>
public sealed class RequestEditor
{
    /// <summary>The longest request name the service stores (CONTRACTS §2: name 1..200).</summary>
    public const int MaximumNameLength = 200;

    private readonly Func<Guid>? _newId;
    private string _savedFingerprint = string.Empty;

    public RequestEditor(SavedRequestDto saved, Func<Guid>? newId = null)
    {
        _newId = newId;
        RequestId = saved.Id;
        Load(saved);
    }

    public Guid RequestId { get; }

    public Guid CollectionId { get; private set; }

    public Guid? FolderId { get; private set; }

    public string Name { get; private set; } = string.Empty;

    public string? Description { get; private set; }

    /// <summary>The version the next save compares with (<c>If-Match</c>). A rename or a move of a clean editor takes the new version.</summary>
    public int Version { get; private set; }

    public RequestDraft Draft { get; private set; } = new();

    /// <summary>True when the draft would send something other than what was saved.</summary>
    public bool IsDirty => Draft.Fingerprint() != _savedFingerprint;

    /// <summary>True after a save was refused with a conflict, until the user reloads or overwrites.</summary>
    public bool Conflicted { get; private set; }

    /// <summary>Takes a saved request as the truth: its name, definition and version. The draft is replaced.</summary>
    public void Load(SavedRequestDto saved)
    {
        CollectionId = saved.CollectionId;
        FolderId = saved.FolderId;
        Name = saved.Name;
        Description = saved.Description;
        Version = saved.Version;
        Draft = RequestDraft.FromDefinition(saved.Request, _newId);
        _savedFingerprint = Draft.Fingerprint();
        Conflicted = false;
    }

    /// <summary>
    /// The service's copy after a rename or a move made from the tree. A clean editor takes the copy whole, so it shows what the
    /// service holds. A dirty one keeps its draft and its version, so its next save is refused as a conflict and the user decides.
    /// </summary>
    public void AcceptServerCopy(SavedRequestDto saved)
    {
        if (!IsDirty)
        {
            Load(saved);
            return;
        }

        CollectionId = saved.CollectionId;
        FolderId = saved.FolderId;
        Name = saved.Name;
    }

    public void MarkConflicted() => Conflicted = true;

    /// <summary>The service's current version, read before an overwrite. The draft stays; the next save carries this version.</summary>
    public void RebaseOnto(int version)
    {
        Version = version;
        Conflicted = false;
    }

    /// <summary>The definition the next save writes.</summary>
    public RequestDefinition Definition => Draft.ToDefinition();

    /// <summary>A request name the service accepts: not blank, and at most 200 characters (CONTRACTS §2).</summary>
    public static bool IsValidName(string? name) => !string.IsNullOrWhiteSpace(name) && name.Trim().Length <= MaximumNameLength;
}
