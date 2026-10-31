using PostmanClone.Web.Api;

namespace PostmanClone.Web.Environments;

/// <summary>What the page shows beside the fields: the messages for the name, for each row, and for the table as a whole.</summary>
/// <param name="Name">The name's message, or null.</param>
/// <param name="Rows">The messages for each row, by the row's id.</param>
/// <param name="Table">Messages about the table as a whole (the limit, or a service message that names no row).</param>
public sealed record EditorIssues(string? Name, IReadOnlyDictionary<Guid, IReadOnlyList<string>> Rows, IReadOnlyList<string> Table)
{
    public bool IsEmpty => Name is null && Rows.Count == 0 && Table.Count == 0;
}

/// <summary>
/// One open environment: its name and its variable rows (ADR-0025). It checks what the service would check, so a save is refused
/// in the page before a request, and it maps the service's own field messages back to the fields. It holds no secret value that
/// the service has not just been given by the user.
/// </summary>
public sealed class EnvironmentEditor
{
    private readonly List<VariableRow> _rows = [];
    private readonly Dictionary<Guid, List<string>> _serverRows = [];
    private readonly List<string> _serverTable = [];
    private string? _serverName;
    private string _savedName = string.Empty;
    private int _storedCount;

    private EnvironmentEditor(Guid id)
    {
        Id = id;
    }

    public Guid Id { get; }

    /// <summary>The environment's name as typed.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>The variable rows, in the order the page shows them.</summary>
    public IReadOnlyList<VariableRow> Rows => _rows;

    /// <summary>Whether the name or any row differs from what the service holds, or a typed secret is waiting to be saved.</summary>
    public bool IsDirty => Name != _savedName || _rows.Count != _storedCount || _rows.Any(row => row.IsChanged);

    public static EnvironmentEditor From(EnvironmentDto dto)
    {
        var editor = new EnvironmentEditor(dto.Id);
        editor.LoadFrom(dto);
        return editor;
    }

    /// <summary>Takes the service's copy after a save, so the rows hold what is stored and no typed secret is left in them.</summary>
    public void MarkSaved(EnvironmentDto dto) => LoadFrom(dto);

    public void AddRow() => _rows.Add(VariableRow.NewPlain());

    public void RemoveRow(Guid rowId) => _rows.RemoveAll(row => row.Id == rowId);

    /// <summary>The client's own checks, as messages, plus the service's messages from the last refused save.</summary>
    public EditorIssues Issues()
    {
        var name = EnvironmentRules.NameProblem(Name.Trim()) ?? _serverName;
        var rows = new Dictionary<Guid, IReadOnlyList<string>>();
        foreach (var row in _rows)
        {
            var messages = new List<string>();
            if (row.OwnProblem() is { } own)
            {
                messages.Add(own);
            }

            if (row.Key.Length > 0 && _rows.Count(other => string.Equals(other.Key, row.Key, StringComparison.Ordinal)) > 1)
            {
                messages.Add(EnvironmentMessages.KeyDuplicate);
            }

            if (_serverRows.TryGetValue(row.Id, out var serverMessages))
            {
                messages.AddRange(serverMessages);
            }

            if (messages.Count > 0)
            {
                rows[row.Id] = messages;
            }
        }

        var table = new List<string>(_serverTable);
        if (_rows.Count > EnvironmentRules.VariablesPerEnvironment)
        {
            table.Insert(0, EnvironmentMessages.VariableLimit);
        }

        return new EditorIssues(name, rows, table);
    }

    /// <summary>True when the client's own checks pass, so a save may be sent. Service messages do not block a retry.</summary>
    public bool CanSave()
    {
        if (EnvironmentRules.NameProblem(Name.Trim()) is not null || _rows.Count > EnvironmentRules.VariablesPerEnvironment)
        {
            return false;
        }

        return _rows.All(row => row.OwnProblem() is null)
            && _rows.All(row => row.Key.Length == 0 || _rows.Count(other => string.Equals(other.Key, row.Key, StringComparison.Ordinal)) == 1);
    }

    /// <summary>The <c>PUT</c> body: the trimmed name and every row. A stored secret with no typed value is kept (keepExistingSecret).</summary>
    public UpdateEnvironmentBody ToUpdateBody() => new(Name.Trim(), _rows.Select(row => row.ToWrite()).ToList());

    /// <summary>
    /// Maps the service's 400 field messages to the page. A message that starts with a row's key (the service's own wording,
    /// "{key}: …") is shown on that row; every other variable message is shown for the table; the name's on the name.
    /// </summary>
    public void ApplyServerErrors(IReadOnlyDictionary<string, string[]> errors)
    {
        ClearServerErrors();
        foreach (var (member, messages) in errors)
        {
            foreach (var message in messages)
            {
                if (string.Equals(member, "name", StringComparison.OrdinalIgnoreCase))
                {
                    _serverName = message;
                    continue;
                }

                var row = _rows.FirstOrDefault(candidate => candidate.Key.Length > 0 && message.StartsWith(candidate.Key + ":", StringComparison.Ordinal));
                if (row is null)
                {
                    _serverTable.Add(message);
                    continue;
                }

                if (!_serverRows.TryGetValue(row.Id, out var list))
                {
                    _serverRows[row.Id] = list = [];
                }

                list.Add(message);
            }
        }
    }

    /// <summary>Forgets the service's messages. Called before each save, so a message is shown only for the save that produced it.</summary>
    public void ClearServerErrors()
    {
        _serverName = null;
        _serverRows.Clear();
        _serverTable.Clear();
    }

    private void LoadFrom(EnvironmentDto dto)
    {
        _rows.Clear();
        _rows.AddRange(dto.Variables.Select(VariableRow.FromStored));
        _storedCount = _rows.Count;
        _savedName = dto.Name;
        Name = dto.Name;
        ClearServerErrors();
    }
}
