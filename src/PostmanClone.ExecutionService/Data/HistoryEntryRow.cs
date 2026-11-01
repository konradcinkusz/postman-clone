namespace PostmanClone.ExecutionService.Data;

/// <summary>
/// One sent request in the history (CONTRACTS §4, ADR-0015). Scoped to the caller's workspace and user: nobody sees
/// another user's history. <see cref="Url"/>, <see cref="RequestJson"/> and <see cref="ResultJson"/> hold masked text
/// only: secret values are replaced before a row is written, and the request is stored as the user wrote it (with
/// <c>{{name}}</c> references), never expanded.
/// </summary>
public sealed class HistoryEntryRow
{
    public Guid Id { get; set; }

    public Guid WorkspaceId { get; set; }

    public Guid UserId { get; set; }

    public DateTimeOffset ExecutedAt { get; set; }

    public string Method { get; set; } = string.Empty;

    public string Url { get; set; } = string.Empty;

    public int? StatusCode { get; set; }

    public string Outcome { get; set; } = string.Empty;

    public int TotalMs { get; set; }

    public long SizeBytes { get; set; }

    /// <summary>The saved request this was sent from, when the caller named one.</summary>
    public Guid? SavedRequestId { get; set; }

    /// <summary>The environment whose variables were applied, when the caller named one.</summary>
    public Guid? EnvironmentId { get; set; }

    public string RequestJson { get; set; } = string.Empty;

    public string ResultJson { get; set; } = string.Empty;

    /// <summary>
    /// <c>server</c> or <c>agent</c> (CONTRACTS A2.5). Rows written before the column existed read as <c>server</c>.
    /// </summary>
    public string Source { get; set; } = "server";
}
