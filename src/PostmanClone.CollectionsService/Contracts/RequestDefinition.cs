namespace PostmanClone.CollectionsService.Contracts;

/// <summary>A key/value row of a request (CONTRACTS §1). Rows are kept in the order the client sent them.</summary>
public sealed record KeyValue(string Key, string Value, bool Enabled = true, string? Description = null);

/// <summary>
/// The body of a request (CONTRACTS §1). <see cref="RawLanguage"/> and <see cref="Raw"/> apply only to a raw body;
/// <see cref="Fields"/> apply only to form bodies. <see cref="Domain.RequestDefinitionValidator"/> checks each rule.
/// </summary>
public sealed record RequestBody(string Type, string? RawLanguage, string? Raw, IReadOnlyList<KeyValue>? Fields);

/// <summary>
/// The auth helper of a request (CONTRACTS §1). Secret fields hold a <c>{{variable}}</c> reference or nothing (ADR-0006);
/// the validator refuses any literal.
/// </summary>
public sealed record RequestAuth(
    string Type,
    string? BearerToken,
    string? Username,
    string? Password,
    string? ApiKeyName,
    string? ApiKeyValue,
    string? ApiKeyIn);

/// <summary>
/// The whole request as the builder edits it and the sender runs it (CONTRACTS §1). Stored as one <c>jsonb</c> document
/// per saved request, exactly as it was validated.
/// </summary>
public sealed record RequestDefinition(
    string Method,
    string Url,
    IReadOnlyList<KeyValue>? Params,
    IReadOnlyList<KeyValue>? Headers,
    RequestBody? Body,
    RequestAuth? Auth);
