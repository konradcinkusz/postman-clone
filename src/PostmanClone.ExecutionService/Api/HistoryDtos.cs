using System.Text.Json.Serialization;
using PostmanClone.Sending.Assembly;
using PostmanClone.Sending.Contracts;

namespace PostmanClone.ExecutionService.Api;

/// <summary>A list page (CONTRACTS §0). The JSON property is <c>page</c>; the C# name cannot repeat the type name.</summary>
public sealed record Page<T>(
    IReadOnlyList<T> Items,
    [property: JsonPropertyName("page")] int PageNumber,
    int PageSize,
    int Total);

/// <summary>One row of the history list (CONTRACTS §4). <c>Url</c> is the masked template.</summary>
public sealed record HistorySummary(
    Guid Id,
    DateTimeOffset ExecutedAt,
    string Method,
    string Url,
    int? StatusCode,
    string Outcome,
    int TotalMs,
    long SizeBytes);

/// <summary>One history entry. The request is the masked template; the result's body is at most 64 KB (CONTRACTS §4).</summary>
public sealed record HistoryEntry(
    Guid Id,
    DateTimeOffset ExecutedAt,
    Guid? SavedRequestId,
    Guid? EnvironmentId,
    RequestDefinition Request,
    ExecutionResult Result);
