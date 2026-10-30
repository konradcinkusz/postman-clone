using Microsoft.EntityFrameworkCore;

namespace PostmanClone.CollectionsService.Data;

/// <summary>
/// Transactions and row locks for the writes that must not interleave: sibling sort orders and the collection limit.
/// On the InMemory provider (tests) there is no transaction and no lock to take; the code path is otherwise the same.
/// </summary>
public static class CollectionsTransactions
{
    /// <summary>
    /// Runs <paramref name="work"/> in one transaction. Npgsql is configured with a retrying execution strategy, which
    /// refuses user-started transactions unless the work runs inside the strategy, so it does.
    /// </summary>
    public static Task<T> RunAsync<T>(CollectionsDbContext db, Func<CancellationToken, Task<T>> work, CancellationToken cancellationToken)
    {
        if (!db.Database.IsRelational())
        {
            return work(cancellationToken);
        }

        return db.Database.CreateExecutionStrategy().ExecuteAsync(async token =>
        {
            await using var transaction = await db.Database.BeginTransactionAsync(token);
            var result = await work(token);
            await transaction.CommitAsync(token);
            return result;
        }, cancellationToken);
    }

    /// <summary>
    /// Takes the collection's row lock until the transaction ends. A no-op write does it: a second writer of the same
    /// collection's folders waits here, then reads the sort orders the first one committed.
    /// </summary>
    public static async Task LockCollectionAsync(CollectionsDbContext db, Guid collectionId, CancellationToken cancellationToken)
    {
        if (!db.Database.IsRelational())
        {
            return;
        }

        await db.Collections
            .Where(collection => collection.Id == collectionId)
            .ExecuteUpdateAsync(setters => setters.SetProperty(collection => collection.SortOrder, collection => collection.SortOrder), cancellationToken);
    }

    /// <summary>
    /// Takes a transaction-scoped advisory lock on the workspace, so two creates in one workspace count the collection
    /// limit one after the other (CONTRACTS §2: 200 collections per workspace).
    /// </summary>
    public static async Task LockWorkspaceAsync(CollectionsDbContext db, Guid workspaceId, CancellationToken cancellationToken)
    {
        if (!db.Database.IsRelational())
        {
            return;
        }

        await db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock(hashtextextended({workspaceId.ToString()}, 0))",
            cancellationToken);
    }
}
