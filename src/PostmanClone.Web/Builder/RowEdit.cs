namespace PostmanClone.Web.Builder;

/// <summary>
/// One change to a key/value row, as the row's input raises it. A null field is unchanged. The page never sees the row's
/// position, only its id, so a row keeps its identity when a row above it is removed.
/// </summary>
public sealed record RowEdit(Guid Id, string? Key, string? Value, bool? Enabled);
