namespace PostmanClone.Web.Api;

// The Web's copies of ExecutionService's history types (CONTRACTS §4, ADR-0015). HistoryRoundTripTests reads the service's own
// types through these copies and writes them back; a renamed or retyped field fails there. The list page is PageResult<T>,
// the same shape CollectionsService returns (CONTRACTS §0).

/// <summary>One row of the history list. <see cref="Url"/> is the masked template (CONTRACTS §4): a secret is never in it.</summary>
public sealed record HistorySummaryDto(
    Guid Id,
    DateTimeOffset ExecutedAt,
    string Method,
    string Url,
    int? StatusCode,
    string Outcome,
    int TotalMs,
    long SizeBytes,
    string Source = "server");

/// <summary>
/// One history entry. The request is the masked template with its <c>{{name}}</c> references; the result was masked by the sender
/// before it was stored, and its body is at most 64 KB (ADR-0015). The page shows both as returned and never unmasks them.
/// </summary>
public sealed record HistoryEntryDto(
    Guid Id,
    DateTimeOffset ExecutedAt,
    Guid? SavedRequestId,
    Guid? EnvironmentId,
    RequestDefinition Request,
    ExecutionResultDto Result,
    string Source = "server");
