using PostmanClone.Web.Api;
using PostmanClone.Web.Builder;

namespace PostmanClone.Web.Collections;

/// <summary>
/// The request editors of one circuit (WP19, ADR-0017): one <see cref="RequestEditor"/> per open request, and the calls that
/// load, save, reload, overwrite, save as and create requests through <see cref="WorkspaceShell"/>. The page owns the tabs and the
/// notices; this class owns what is held and what the service was told. Failures are outcomes, never exceptions.
/// </summary>
public sealed class RequestWorkbench(WorkspaceShell shell)
{
    private readonly Dictionary<Guid, RequestEditor> _editors = [];

    public bool TryGet(Guid requestId, out RequestEditor editor) => _editors.TryGetValue(requestId, out editor!);

    /// <summary>Every editor the circuit holds, for the page to look at (for instance to find the dirty tabs).</summary>
    public IReadOnlyCollection<RequestEditor> Editors => _editors.Values;

    /// <summary>Forgets an editor, when its tab closes or its request is deleted. Its unsaved draft goes with it.</summary>
    public void Forget(Guid requestId) => _editors.Remove(requestId);

    /// <summary>Returns the open editor of a request, reading the request from the service when there is none yet.</summary>
    public async Task<CollectionAnswer<RequestEditor>> OpenAsync(Guid requestId, CancellationToken cancellationToken)
    {
        if (_editors.TryGetValue(requestId, out var existing))
        {
            return new CollectionAnswer<RequestEditor>(CollectionOutcome.Succeeded, existing, null);
        }

        var answer = await shell.LoadRequestAsync(requestId, cancellationToken);
        if (answer.Value is not { } saved)
        {
            return new CollectionAnswer<RequestEditor>(answer.Outcome, null, answer.Code);
        }

        var editor = new RequestEditor(saved);
        _editors[requestId] = editor;
        return new CollectionAnswer<RequestEditor>(CollectionOutcome.Succeeded, editor, null);
    }

    /// <summary>
    /// Saves the draft with the version the editor read. A refused check of the draft is answered as <see cref="CollectionOutcome.Invalid"/>
    /// with no code, and nothing is sent. A conflict marks the editor, so the page can offer reload or overwrite.
    /// </summary>
    public async Task<CollectionAnswer<RequestEditor>> SaveAsync(Guid requestId, CancellationToken cancellationToken)
    {
        if (!_editors.TryGetValue(requestId, out var editor))
        {
            return new CollectionAnswer<RequestEditor>(CollectionOutcome.NotFound, null, null);
        }

        if (editor.Draft.Validate().Count > 0 || !RequestEditor.IsValidName(editor.Name))
        {
            return new CollectionAnswer<RequestEditor>(CollectionOutcome.Invalid, null, null);
        }

        var answer = await shell.SaveRequestAsync(requestId, editor.Name, editor.Description, editor.Definition, editor.Version, cancellationToken);
        if (answer.Value is { } saved)
        {
            editor.Load(saved);
            return new CollectionAnswer<RequestEditor>(CollectionOutcome.Succeeded, editor, null);
        }

        if (answer.Outcome == CollectionOutcome.Conflict)
        {
            editor.MarkConflicted();
        }

        return new CollectionAnswer<RequestEditor>(answer.Outcome, null, answer.Code);
    }

    /// <summary>Takes the service's version of the request in place of the draft. The draft is discarded (the user chose it).</summary>
    public async Task<CollectionAnswer<RequestEditor>> ReloadAsync(Guid requestId, CancellationToken cancellationToken)
    {
        var answer = await shell.LoadRequestAsync(requestId, cancellationToken);
        if (answer.Value is not { } saved || !_editors.TryGetValue(requestId, out var editor))
        {
            return new CollectionAnswer<RequestEditor>(answer.Outcome, null, answer.Code);
        }

        editor.Load(saved);
        return new CollectionAnswer<RequestEditor>(CollectionOutcome.Succeeded, editor, null);
    }

    /// <summary>
    /// Writes the draft over the service's newer version (the user chose to overwrite it). The version is read first, and the
    /// write carries it, so the service's change is replaced on purpose and not by accident.
    /// </summary>
    public async Task<CollectionAnswer<RequestEditor>> OverwriteAsync(Guid requestId, CancellationToken cancellationToken)
    {
        var current = await shell.LoadRequestAsync(requestId, cancellationToken);
        if (current.Value is not { } fresh || !_editors.TryGetValue(requestId, out var editor))
        {
            return new CollectionAnswer<RequestEditor>(current.Outcome, null, current.Code);
        }

        editor.RebaseOnto(fresh.Version);
        return await SaveAsync(requestId, cancellationToken);
    }

    /// <summary>
    /// Saves the draft as a new request in a collection (and folder). The source keeps its own draft and stays dirty, so nothing the
    /// user typed is lost by a save as. The new request gets an editor of its own.
    /// </summary>
    public async Task<CollectionAnswer<RequestEditor>> SaveAsNewAsync(Guid sourceId, string name, Guid collectionId, Guid? folderId, CancellationToken cancellationToken)
    {
        if (!_editors.TryGetValue(sourceId, out var source))
        {
            return new CollectionAnswer<RequestEditor>(CollectionOutcome.NotFound, null, null);
        }

        var trimmed = name.Trim();
        if (!RequestEditor.IsValidName(trimmed) || source.Draft.Validate().Count > 0)
        {
            return new CollectionAnswer<RequestEditor>(CollectionOutcome.Invalid, null, null);
        }

        var answer = await shell.CreateRequestAsync(collectionId, folderId, trimmed, null, source.Definition, cancellationToken);
        if (answer.Value is not { } created)
        {
            return new CollectionAnswer<RequestEditor>(answer.Outcome, null, answer.Code);
        }

        var editor = new RequestEditor(created);
        _editors[created.Id] = editor;
        return new CollectionAnswer<RequestEditor>(CollectionOutcome.Succeeded, editor, null);
    }

    /// <summary>Creates an untitled request in a collection or a folder, with the default definition, and gives it an editor.</summary>
    public async Task<CollectionAnswer<RequestEditor>> CreateUntitledAsync(Guid collectionId, Guid? folderId, CancellationToken cancellationToken)
    {
        var answer = await shell.CreateRequestAsync(collectionId, folderId, BuilderMessages.UntitledName, null, BuilderDefaults.NewDefinition(), cancellationToken);
        if (answer.Value is not { } created)
        {
            return new CollectionAnswer<RequestEditor>(answer.Outcome, null, answer.Code);
        }

        var editor = new RequestEditor(created);
        _editors[created.Id] = editor;
        return new CollectionAnswer<RequestEditor>(CollectionOutcome.Succeeded, editor, null);
    }

    /// <summary>After a rename or a move made from the tree, the editor takes the service's copy (see <see cref="RequestEditor.AcceptServerCopy"/>).</summary>
    public void AcceptServerCopy(SavedRequestDto saved)
    {
        if (_editors.TryGetValue(saved.Id, out var editor))
        {
            editor.AcceptServerCopy(saved);
        }
    }
}
