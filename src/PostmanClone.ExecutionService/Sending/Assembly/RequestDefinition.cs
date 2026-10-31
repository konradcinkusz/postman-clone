namespace PostmanClone.ExecutionService.Sending.Assembly;

/// <summary>
/// The request as the user saved it (CONTRACTS §1, <c>RequestDefinition</c>), before variables are applied. Strings may
/// hold <c>{{name}}</c> references. This is a copy of the shape: the endpoint that binds it arrives in a later work package.
/// </summary>
public sealed record RequestDefinition(
    string Method,
    string Url,
    IReadOnlyList<KeyValueRow> Params,
    IReadOnlyList<KeyValueRow> Headers,
    RequestBody Body,
    RequestAuth Auth);

/// <summary>One row of a key/value list. Disabled rows and rows with an empty key are ignored.</summary>
public sealed record KeyValueRow(string Key, string Value, bool Enabled = true);

public sealed record RequestBody(BodyType Type, RawLanguage? RawLanguage, string? Raw, IReadOnlyList<KeyValueRow> Fields);

public enum BodyType
{
    None,
    Raw,
    FormUrlEncoded,
    FormData,
}

public enum RawLanguage
{
    Text,
    Json,
    Xml,
    Html,
    Javascript,
}

/// <summary>
/// The auth helper. Its secret fields (<c>BearerToken</c>, <c>Password</c>, <c>ApiKeyValue</c>) accept a
/// <c>{{name}}</c> reference only (ADR-0006); anything else is refused with <c>secret_literal_not_allowed</c>.
/// </summary>
public sealed record RequestAuth(
    AuthType Type,
    string? BearerToken,
    string? Username,
    string? Password,
    string? ApiKeyName,
    string? ApiKeyValue,
    ApiKeyLocation? ApiKeyIn);

public enum AuthType
{
    None,
    Bearer,
    Basic,
    ApiKey,
}

public enum ApiKeyLocation
{
    Header,
    Query,
}
