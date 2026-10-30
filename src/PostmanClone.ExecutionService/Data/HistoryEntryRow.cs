namespace PostmanClone.ExecutionService.Data;

/// <summary>
/// One sent request in the history (CONTRACTS §4). Scoped to the caller's workspace and user: nobody sees another
/// user's history. <see cref="Url"/>, <see cref="RequestJson"/> and <see cref="ResultJson"/> hold masked text only
/// (secret values are replaced before a row is written, WP15).
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

    public string RequestJson { get; set; } = string.Empty;

    public string ResultJson { get; set; } = string.Empty;
}
