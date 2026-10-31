using PostmanClone.ExecutionService.Sending.Assembly;

namespace PostmanClone.ExecutionService.History;

/// <summary>
/// The request as the history keeps it (CONTRACTS §4, ADR-0015). The template keeps its <c>{{name}}</c> references, so the
/// caller can re-open it, and nothing here expands one. Every secret form is masked in the text. A sensitive header, query
/// or form name has its whole value replaced. The auth helper keeps its type, key name and placement, and drops every
/// value (bearer token, username, password, API key value), because CONTRACTS §4 says auth-helper values are never stored.
/// </summary>
public static class HistoryRedaction
{
    public static RequestDefinition Template(RequestDefinition request, SecretMasker masker) =>
        new(
            masker.MaskText(request.Method),
            masker.MaskUrl(request.Url),
            Rows(request.Params, masker),
            Rows(request.Headers, masker),
            Body(request.Body, masker),
            Auth(request.Auth));

    private static IReadOnlyList<KeyValueRow> Rows(IEnumerable<KeyValueRow>? rows, SecretMasker masker) =>
        (rows ?? []).Select(row => Row(row, masker)).ToList();

    private static KeyValueRow Row(KeyValueRow row, SecretMasker masker) => row with
    {
        Key = masker.MaskText(row.Key),
        Value = SecretMasker.IsSensitiveName(row.Key) ? SecretMasker.Mask : masker.MaskText(row.Value),
    };

    private static RequestBody Body(RequestBody body, SecretMasker masker) => body with
    {
        Raw = body.Raw is null ? null : masker.MaskText(body.Raw),
        Fields = Rows(body.Fields, masker),
    };

    private static RequestAuth Auth(RequestAuth auth) => auth with
    {
        BearerToken = null,
        Username = null,
        Password = null,
        ApiKeyValue = null,
    };
}
