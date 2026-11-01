using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Options;
using PostmanClone.ExecutionService.Data;
using PostmanClone.ExecutionService.Options;
using PostmanClone.Sending.Execution;

namespace PostmanClone.ExecutionService.History;

/// <summary>
/// Stores one execution in the caller's history (CONTRACTS §4, ADR-0015). The row holds the masked template and the masked
/// result. On a relational provider the insert and the trim to the retention limit run in one transaction, inside the
/// provider's execution strategy, so a transient failure is retried as a whole. The user never holds more than
/// <c>HistoryRetentionPerUserWorkspace</c> entries once the insert commits. Two sends that race at the limit may leave one
/// extra entry until the next insert trims it; neither send fails.
/// </summary>
public sealed class HistoryRecorder(ExecutionDbContext db, TimeProvider clock, IOptions<ExecutionOptions> options) : IHistoryRecorder
{
    public async Task<Guid?> RecordAsync(HistoryDraft draft, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(draft.UserId, out var userId))
        {
            throw new InvalidOperationException("The caller's subject is not a user id, so the entry has no owner.");
        }

        var row = HistoryMapping.ToRow(Guid.NewGuid(), clock.GetUtcNow(), userId, draft, options.Value.HistoryBodyMaxBytes);
        if (!db.Database.IsRelational())
        {
            await StoreAsync(row, cancellationToken);
            return row.Id;
        }

        // The retry belongs to the execution strategy; a user-initiated transaction is only allowed inside it.
        await db.Database.CreateExecutionStrategy().ExecuteAsync(
            token => StoreAsync(row, token),
            cancellationToken);
        return row.Id;
    }

    /// <summary>Inserts the row and trims the caller's older entries. Safe to run again after a failed attempt.</summary>
    private async Task StoreAsync(HistoryEntryRow row, CancellationToken cancellationToken)
    {
        // Marked again on every attempt: an attempt that failed after its insert was rolled back, and the tracker still
        // thinks the row is saved, so the state is set rather than assumed.
        db.Entry(row).State = EntityState.Added;

        await using IDbContextTransaction? transaction = db.Database.IsRelational()
            ? await db.Database.BeginTransactionAsync(cancellationToken)
            : null;

        await db.SaveChangesAsync(cancellationToken);

        // Newest first; ties broken by id so the cut is the same on every run. Entries past the limit are removed.
        var retention = options.Value.HistoryRetentionPerUserWorkspace;
        var stale = await HistoryScope.For(db, row.WorkspaceId, row.UserId)
            .OrderByDescending(entry => entry.ExecutedAt)
            .ThenByDescending(entry => entry.Id)
            .Skip(retention)
            .Select(entry => entry.Id)
            .ToListAsync(cancellationToken);

        if (stale.Count > 0)
        {
            await HistoryDeletion.DeleteAsync(db, db.HistoryEntries.Where(entry => stale.Contains(entry.Id)), cancellationToken);
        }

        if (transaction is not null)
        {
            await transaction.CommitAsync(cancellationToken);
        }
    }
}
