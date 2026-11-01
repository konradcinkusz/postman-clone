using PostmanClone.Sending.Contracts;
using PostmanClone.Sending.Assembly;

namespace PostmanClone.ExecutionService.History;

/// <summary>
/// What a history entry is made of (ADR-0015): the request as the user wrote it, with <c>{{name}}</c> references and never
/// expanded values; the masker built from the request's secret values, so the request can be masked in every form; and the
/// result, which the sender has already masked.
/// </summary>
public sealed record HistoryDraft(
    Guid WorkspaceId,
    string UserId,
    Guid? SavedRequestId,
    Guid? EnvironmentId,
    RequestDefinition Request,
    SecretMasker Masker,
    ExecutionResult Result);

/// <summary>Records one execution in the caller's history and returns the new entry's id. A failure is caught by the caller, which then reports no id.</summary>
public interface IHistoryRecorder
{
    Task<Guid?> RecordAsync(HistoryDraft draft, CancellationToken cancellationToken);
}
