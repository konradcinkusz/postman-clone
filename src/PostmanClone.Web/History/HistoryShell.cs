using PostmanClone.Web.Api;
using PostmanClone.Web.Auth;

namespace PostmanClone.Web.History;

/// <summary>
/// The history pages' calls to ExecutionService (WP22). Each call takes the circuit's access token from <see cref="CircuitSession"/>,
/// so the circuit holds no token of its own; the service answers only for the caller's entries in the token's workspace. No workspace
/// and no session are outcomes, so the page can say which. The page renders what comes back.
/// </summary>
public sealed class HistoryShell(WorkspaceState workspace, CircuitSession session, IHistoryClient history)
{
    public Task<CollectionAnswer<PageResult<HistorySummaryDto>>> ListAsync(int page, int pageSize, string? query, CancellationToken cancellationToken) =>
        WithTokenAsync((token, ct) => history.ListAsync(token, page, pageSize, query, ct), cancellationToken);

    public Task<CollectionAnswer<HistoryEntryDto>> GetAsync(Guid entryId, CancellationToken cancellationToken) =>
        WithTokenAsync((token, ct) => history.GetAsync(token, entryId, ct), cancellationToken);

    public Task<CollectionAnswer> DeleteAsync(Guid entryId, CancellationToken cancellationToken) =>
        WithTokenAsync((token, ct) => history.DeleteAsync(token, entryId, ct), cancellationToken);

    public Task<CollectionAnswer> ClearAsync(CancellationToken cancellationToken) =>
        WithTokenAsync((token, ct) => history.ClearAsync(token, ct), cancellationToken);

    private async Task<CollectionAnswer<T>> WithTokenAsync<T>(Func<string, CancellationToken, Task<CollectionAnswer<T>>> call, CancellationToken cancellationToken)
    {
        if (!workspace.Resolution.IsActive)
        {
            return new CollectionAnswer<T>(CollectionOutcome.NoWorkspace, default, null);
        }

        var token = await session.AccessTokenAsync(cancellationToken);
        return token is null
            ? new CollectionAnswer<T>(CollectionOutcome.SessionEnded, default, null)
            : await call(token, cancellationToken);
    }

    private async Task<CollectionAnswer> WithTokenAsync(Func<string, CancellationToken, Task<CollectionAnswer>> call, CancellationToken cancellationToken)
    {
        if (!workspace.Resolution.IsActive)
        {
            return new CollectionAnswer(CollectionOutcome.NoWorkspace, null);
        }

        var token = await session.AccessTokenAsync(cancellationToken);
        return token is null
            ? new CollectionAnswer(CollectionOutcome.SessionEnded, null)
            : await call(token, cancellationToken);
    }
}
