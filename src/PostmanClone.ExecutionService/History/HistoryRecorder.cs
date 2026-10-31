using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Options;
using PostmanClone.ExecutionService.Data;
using PostmanClone.ExecutionService.Options;
using PostmanClone.ExecutionService.Sending.Execution;

namespace PostmanClone.ExecutionService.History;

/// <summary>
/// Stores one execution in the caller's history (CONTRACTS §4, ADR-0015). The row holds the masked template and the masked
/// result. The insert and the trim to the retention limit run in one transaction, so a user never holds more than
/// <c>HistoryRetentionPerUserWorkspace</c> entries once the insert commits (the oldest are removed). Two sends that race at
/// the limit may leave one extra entry until the next insert trims it; neither send fails.
/// </summary>
public sealed class HistoryRecorder(ExecutionDbContext db, TimeProvider clock, IOptions<ExecutionOptions> options) : IHistoryRecorder
{
    public async Task<Guid?> RecordAsync(HistoryDraft draft, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(draft.UserId, out var userId))
        {
            throw new InvalidOperationException("The caller's subject is not a user id, so the entry has no owner.");
        }

        var limits = options.Value;
        var id = Guid.NewGuid();
        var row = HistoryMapping.ToRow(id, clock.GetUtcNow(), userId, draft, limits.HistoryBodyMaxBytes);

        // The transaction is for providers that have one. The InMemory provider has none, and it is test-only.
        await using IDbContextTransaction? transaction = db.Database.IsRelational()
            ? await db.Database.BeginTransactionAsync(cancellationToken)
            : null;

        db.HistoryEntries.Add(row);
        await db.SaveChangesAsync(cancellationToken);

        // Newest first; ties broken by id so the cut is the same on every run. Entries past the limit are removed.
        var stale = await HistoryScope.For(db, draft.WorkspaceId, userId)
            .OrderByDescending(entry => entry.ExecutedAt)
            .ThenByDescending(entry => entry.Id)
            .Skip(limits.HistoryRetentionPerUserWorkspace)
            .Select(entry => entry.Id)
            .ToListAsync(cancellationToken);

        // ExecuteDelete does not check the affected count, so two racing trims of the same row do not fail.
        if (stale.Count > 0)
        {
            await db.HistoryEntries.Where(entry => stale.Contains(entry.Id)).ExecuteDeleteAsync(cancellationToken);
        }

        if (transaction is not null)
        {
            await transaction.CommitAsync(cancellationToken);
        }

        return id;
    }
}
