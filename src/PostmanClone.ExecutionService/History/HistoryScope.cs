using PostmanClone.ExecutionService.Data;

namespace PostmanClone.ExecutionService.History;

/// <summary>
/// The rows one caller may see or change: those of the caller's workspace and of the caller's own user id (CONTRACTS §4).
/// Every history query starts here, so no query can forget either half of the scope.
/// </summary>
public static class HistoryScope
{
    public static IQueryable<HistoryEntryRow> For(ExecutionDbContext db, Guid workspaceId, Guid userId) =>
        db.HistoryEntries.Where(entry => entry.WorkspaceId == workspaceId && entry.UserId == userId);
}
