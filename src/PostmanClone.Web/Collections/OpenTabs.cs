namespace PostmanClone.Web.Collections;

/// <summary>One open request tab. <see cref="Dirty"/> is set when the tab holds changes not yet saved (the builder, WP19).</summary>
public sealed record OpenTab(Guid RequestId, string Title, bool Dirty);

public enum OpenTabResult
{
    /// <summary>A new tab was opened and is now the active one.</summary>
    Opened,

    /// <summary>The request already had a tab; that tab is now active.</summary>
    Activated,

    /// <summary>The limit is reached and the request has no tab. Nothing changed.</summary>
    Full,

    /// <summary>No tab is open for that request. Nothing changed.</summary>
    NotOpen,
}

public enum CloseTabResult
{
    Closed,

    /// <summary>The tab holds unsaved changes and the caller did not say to discard them. The tab stays open.</summary>
    Dirty,

    NotOpen,
}

/// <summary>
/// The open request tabs of one circuit: open, close, activate, a dirty flag, and at most <see cref="MaximumOpen"/> of
/// them (WP18). Plain state with no renderer, so the rules are tested without a browser (BLAZOR-WEB-APP §10).
/// </summary>
public sealed class OpenTabs
{
    /// <summary>How many request tabs may be open at once. A request opened past this is refused, not replaced.</summary>
    public const int MaximumOpen = 20;

    private readonly List<OpenTab> _tabs = [];

    public IReadOnlyList<OpenTab> Tabs => _tabs;

    /// <summary>The id of the active tab, or null when none is open.</summary>
    public Guid? ActiveRequestId { get; private set; }

    public OpenTab? Active => ActiveRequestId is { } id ? _tabs.Find(tab => tab.RequestId == id) : null;

    public OpenTabResult Open(Guid requestId, string title)
    {
        if (_tabs.Exists(tab => tab.RequestId == requestId))
        {
            ActiveRequestId = requestId;
            return OpenTabResult.Activated;
        }

        if (_tabs.Count >= MaximumOpen)
        {
            return OpenTabResult.Full;
        }

        _tabs.Add(new OpenTab(requestId, title, Dirty: false));
        ActiveRequestId = requestId;
        return OpenTabResult.Opened;
    }

    public OpenTabResult Activate(Guid requestId)
    {
        if (!_tabs.Exists(tab => tab.RequestId == requestId))
        {
            return OpenTabResult.NotOpen;
        }

        ActiveRequestId = requestId;
        return OpenTabResult.Activated;
    }

    /// <summary>
    /// Closes a tab. A dirty tab stays open unless <paramref name="discardChanges"/> is set. Closing the active tab makes the
    /// tab that takes its place active (the one to its right, or the last one).
    /// </summary>
    public CloseTabResult Close(Guid requestId, bool discardChanges = false)
    {
        var index = _tabs.FindIndex(tab => tab.RequestId == requestId);
        if (index < 0)
        {
            return CloseTabResult.NotOpen;
        }

        if (_tabs[index].Dirty && !discardChanges)
        {
            return CloseTabResult.Dirty;
        }

        _tabs.RemoveAt(index);
        if (ActiveRequestId == requestId)
        {
            ActiveRequestId = _tabs.Count == 0 ? null : _tabs[Math.Min(index, _tabs.Count - 1)].RequestId;
        }

        return CloseTabResult.Closed;
    }

    /// <summary>Marks a tab as holding unsaved changes, or as saved.</summary>
    public bool SetDirty(Guid requestId, bool dirty)
    {
        var index = _tabs.FindIndex(tab => tab.RequestId == requestId);
        if (index < 0)
        {
            return false;
        }

        _tabs[index] = _tabs[index] with { Dirty = dirty };
        return true;
    }

    /// <summary>Shows a renamed request under its new name. The tab keeps its place and its state.</summary>
    public bool Retitle(Guid requestId, string title)
    {
        var index = _tabs.FindIndex(tab => tab.RequestId == requestId);
        if (index < 0)
        {
            return false;
        }

        _tabs[index] = _tabs[index] with { Title = title };
        return true;
    }
}
