using System.Text.Json;
using PostmanClone.Web.Api;

namespace PostmanClone.Web.Builder;

/// <summary>
/// One row of a key/value list in the editor. <see cref="Id"/> is stable across edits, so the list renders the row it
/// showed before; it is never sent anywhere.
/// </summary>
public sealed record DraftRow(Guid Id, string Key, string Value, bool Enabled);

/// <summary>The four lists the editor edits as key/value rows.</summary>
public enum RowList
{
    Params,
    Headers,
    FormFields,
}

/// <summary>
/// An editable copy of a <see cref="RequestDefinition"/> (CONTRACTS §1), built for the builder (WP19, ADR-0017). It holds every
/// field the editor shows and keeps the URL bar and the parameter table in step.
/// <para>
/// The stored form has no query in its URL: the query lives in <c>params</c>, and the sender appends the enabled rows
/// (ExecutionService, <c>UrlBuilder</c>). A query the URL bar shows is therefore a view of the table. <see cref="Url"/> is the
/// text the URL bar holds: what the user typed, or the composed text after a table edit. <see cref="ToDefinition"/> writes the
/// base and the fragment, and the table as the params, so a query is never sent twice.
/// </para>
/// <para>
/// A body or an auth helper keeps only the fields its type uses, so a value typed under one type never leaks into another.
/// Plain state with no renderer, so the rules are tested without a browser (BLAZOR-WEB-APP §10).
/// </para>
/// </summary>
public sealed class RequestDraft
{
    private readonly Func<Guid> _newId;

    /// <summary>The fingerprint of what was saved last: the definition as it was read, or as it was last written.</summary>
    private string _saved = string.Empty;

    public RequestDraft(Func<Guid>? newId = null)
    {
        _newId = newId ?? Guid.NewGuid;
    }

    public string Method { get; private set; } = "GET";

    /// <summary>The text the URL bar shows: the URL with the enabled query rows (see the class summary).</summary>
    public string Url { get; private set; } = string.Empty;

    public IReadOnlyList<DraftRow> Params { get; private set; } = [];

    public IReadOnlyList<DraftRow> Headers { get; private set; } = [];

    /// <summary>none, raw, formUrlEncoded or formData.</summary>
    public string BodyType { get; private set; } = "none";

    /// <summary>json, text, xml, html or javascript. Used only by a raw body.</summary>
    public string RawLanguage { get; private set; } = "json";

    public string Raw { get; private set; } = string.Empty;

    /// <summary>The text fields of a form body (file upload is out of scope, ADR-0001).</summary>
    public IReadOnlyList<DraftRow> FormFields { get; private set; } = [];

    /// <summary>none, bearer, basic or apiKey.</summary>
    public string AuthType { get; private set; } = "none";

    public string BearerToken { get; private set; } = string.Empty;

    public string Username { get; private set; } = string.Empty;

    public string Password { get; private set; } = string.Empty;

    public string ApiKeyName { get; private set; } = string.Empty;

    public string ApiKeyValue { get; private set; } = string.Empty;

    /// <summary>header or query.</summary>
    public string ApiKeyIn { get; private set; } = "header";

    /// <summary>Reads a definition into the editor. Null lists and objects count as empty, as the service reads them (CONTRACTS §1).</summary>
    public static RequestDraft FromDefinition(RequestDefinition definition, Func<Guid>? newId = null)
    {
        var draft = new RequestDraft(newId);
        var ids = draft._newId;
        draft.Method = definition.Method;
        // A query the stored URL still carries (a cURL import keeps it there) is moved into the table first, in front of the
        // params, which is the order the sender uses. The saved baseline is taken after this, so the move alone is not an edit.
        var split = UrlParamSync.Split(definition.Url);
        var urlRows = split.Pairs.Select(pair => new DraftRow(ids(), pair.Key, pair.Value, Enabled: true)).ToList();
        draft.Params = [.. urlRows, .. ToRows(definition.Params, ids)];
        draft.Url = UrlParamSync.Join(split.Base, UrlParamSync.PairsOf(draft.Params), split.Fragment);
        draft.Headers = ToRows(definition.Headers, ids);
        draft.BodyType = definition.Body?.Type ?? "none";
        draft.RawLanguage = definition.Body?.RawLanguage ?? "json";
        draft.Raw = definition.Body?.Raw ?? string.Empty;
        draft.FormFields = ToRows(definition.Body?.Fields, ids);
        draft.AuthType = definition.Auth?.Type ?? "none";
        draft.BearerToken = definition.Auth?.BearerToken ?? string.Empty;
        draft.Username = definition.Auth?.Username ?? string.Empty;
        draft.Password = definition.Auth?.Password ?? string.Empty;
        draft.ApiKeyName = definition.Auth?.ApiKeyName ?? string.Empty;
        draft.ApiKeyValue = definition.Auth?.ApiKeyValue ?? string.Empty;
        draft.ApiKeyIn = definition.Auth?.ApiKeyIn ?? "header";
        draft._saved = draft.Fingerprint();
        return draft;
    }

    /// <summary>
    /// The definition in canonical form: the URL without its query (the table carries the query), the fields of the chosen body
    /// and auth type only.
    /// </summary>
    public RequestDefinition ToDefinition() => new(
        Method,
        StoredUrl(),
        ToKeyValues(Params),
        ToKeyValues(Headers),
        BuildBody(),
        BuildAuth());

    /// <summary>The URL as stored: its base and its fragment. The query is in the table.</summary>
    public string StoredUrl()
    {
        var split = UrlParamSync.Split(Url);
        return UrlParamSync.Join(split.Base, [], split.Fragment);
    }

    /// <summary>A stable text of <see cref="ToDefinition"/>. Two drafts with the same text send the same request.</summary>
    public string Fingerprint() => JsonSerializer.Serialize(ToDefinition(), JsonOptions);

    /// <summary>True when the draft would send something other than what was saved (see <see cref="MarkSaved"/>).</summary>
    public bool IsDirty => Fingerprint() != _saved;

    /// <summary>Takes the draft as saved: from now on it is clean until it changes again.</summary>
    public void MarkSaved() => _saved = Fingerprint();

    /// <summary>The rules the service enforces, judged on this draft (see <see cref="DraftValidator"/>).</summary>
    public IReadOnlyList<DraftIssue> Validate() => DraftValidator.Validate(ToDefinition());

    public void SetMethod(string method) => Method = method;

    /// <summary>
    /// A URL edit. The parameter table follows the query the text carries; the text itself is kept exactly as typed, so the
    /// URL bar never rewrites what the user is typing.
    /// </summary>
    public void SetUrl(string url)
    {
        Url = url;
        Params = UrlParamSync.MergeFromUrl(Params, UrlParamSync.Split(url).Pairs, _newId);
    }

    public void SetBodyType(string bodyType)
    {
        BodyType = bodyType;
        if (bodyType == "formUrlEncoded" || bodyType == "formData")
        {
            return;
        }

        FormFields = [];
    }

    public void SetRawLanguage(string language) => RawLanguage = language;

    public void SetRaw(string raw) => Raw = raw;

    public void SetAuthType(string authType) => AuthType = authType;

    public void SetBearerToken(string value) => BearerToken = value;

    public void SetUsername(string value) => Username = value;

    public void SetPassword(string value) => Password = value;

    public void SetApiKeyName(string value) => ApiKeyName = value;

    public void SetApiKeyValue(string value) => ApiKeyValue = value;

    public void SetApiKeyIn(string value) => ApiKeyIn = value;

    /// <summary>Adds an empty enabled row at the end of a list, and returns its id.</summary>
    public Guid AddRow(RowList list)
    {
        var id = _newId();
        Replace(list, rows => [.. rows, new DraftRow(id, string.Empty, string.Empty, Enabled: true)]);
        return id;
    }

    /// <summary>Changes one row. Null arguments keep the current value.</summary>
    public void EditRow(RowList list, Guid rowId, string? key = null, string? value = null, bool? enabled = null)
    {
        Replace(list, rows => rows.Select(row => row.Id == rowId
            ? row with { Key = key ?? row.Key, Value = value ?? row.Value, Enabled = enabled ?? row.Enabled }
            : row).ToList());
    }

    public void RemoveRow(RowList list, Guid rowId) => Replace(list, rows => rows.Where(row => row.Id != rowId).ToList());

    /// <summary>Replaces a whole list, for the bulk-edit text. The caller supplies the rows and their ids.</summary>
    public void ReplaceRows(RowList list, IReadOnlyList<DraftRow> rows) => Replace(list, _ => rows.ToList());

    public IReadOnlyList<DraftRow> RowsOf(RowList list) => list switch
    {
        RowList.Params => Params,
        RowList.Headers => Headers,
        _ => FormFields,
    };

    /// <summary>Generates an id for a row a caller creates, from the same source the draft uses.</summary>
    public Guid NewId() => _newId();

    private void Replace(RowList list, Func<IReadOnlyList<DraftRow>, List<DraftRow>> change)
    {
        switch (list)
        {
            case RowList.Params:
                Params = change(Params);
                Url = UrlParamSync.Compose(Url, UrlParamSync.PairsOf(Params));
                break;
            case RowList.Headers:
                Headers = change(Headers);
                break;
            default:
                FormFields = change(FormFields);
                break;
        }
    }

    private RequestBody BuildBody() => BodyType switch
    {
        "raw" => new RequestBody("raw", RawLanguage, Raw, []),
        "formUrlEncoded" or "formData" => new RequestBody(BodyType, null, null, ToKeyValues(FormFields)),
        _ => new RequestBody("none", null, null, []),
    };

    private RequestAuth BuildAuth() => AuthType switch
    {
        "bearer" => new RequestAuth("bearer", BearerToken, null, null, null, null, null),
        "basic" => new RequestAuth("basic", null, Username, Password, null, null, null),
        "apiKey" => new RequestAuth("apiKey", null, null, null, ApiKeyName, ApiKeyValue, ApiKeyIn),
        _ => new RequestAuth("none", null, null, null, null, null, null),
    };

    private static List<KeyValue> ToKeyValues(IEnumerable<DraftRow> rows) =>
        rows.Select(row => new KeyValue(row.Key, row.Value, row.Enabled)).ToList();

    /// <summary>Gives each row of a definition a fresh id. Null rows count as empty, as the service reads them.</summary>
    private static List<DraftRow> ToRows(List<KeyValue>? rows, Func<Guid> newId) =>
        (rows ?? []).Select(row => new DraftRow(newId(), row.Key, row.Value, row.Enabled)).ToList();

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
}
