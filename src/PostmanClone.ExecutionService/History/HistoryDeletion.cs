using Microsoft.EntityFrameworkCore;
using PostmanClone.ExecutionService.Data;

namespace PostmanClone.ExecutionService.History;

/// <summary>
/// Deletes the rows of a query and returns how many went. On a relational provider this is one <c>DELETE</c> statement, which
/// does not fail when another request has already removed a row. The InMemory provider, which only the tests use, cannot run
/// <c>ExecuteDelete</c>, so there the rows are loaded and removed through the change tracker.
/// </summary>
public static class HistoryDeletion
{
    public static async Task<int> DeleteAsync(ExecutionDbContext db, IQueryable<HistoryEntryRow> rows, CancellationToken cancellationToken)
    {
        if (db.Database.IsRelational())
        {
            return await rows.ExecuteDeleteAsync(cancellationToken);
        }

        var found = await rows.ToListAsync(cancellationToken);
        db.HistoryEntries.RemoveRange(found);
        await db.SaveChangesAsync(cancellationToken);
        return found.Count;
    }
}
