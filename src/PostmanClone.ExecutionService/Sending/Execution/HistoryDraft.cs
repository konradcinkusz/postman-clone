using PostmanClone.ExecutionService.Api;
using PostmanClone.ExecutionService.Sending.Assembly;

namespace PostmanClone.ExecutionService.Sending.Execution;

/// <summary>
/// What a history entry is made of: the request as the user saved it (with references, never expanded values) and the
/// masked result. WP15 stores it; until then <see cref="NullHistoryRecorder"/> drops it.
/// </summary>
public sealed record HistoryDraft(
    Guid WorkspaceId,
    string UserId,
    Guid? SavedRequestId,
    Guid? EnvironmentId,
    RequestDefinition Request,
    ExecutionResult Result);

/// <summary>Records one execution in the caller's history and returns the new entry's id, or null when nothing was stored.</summary>
public interface IHistoryRecorder
{
    Task<Guid?> RecordAsync(HistoryDraft draft, CancellationToken cancellationToken);
}

/// <summary>The history seam until WP15: records nothing, so <c>historyId</c> is always null.</summary>
public sealed class NullHistoryRecorder : IHistoryRecorder
{
    public Task<Guid?> RecordAsync(HistoryDraft draft, CancellationToken cancellationToken) => Task.FromResult<Guid?>(null);
}
