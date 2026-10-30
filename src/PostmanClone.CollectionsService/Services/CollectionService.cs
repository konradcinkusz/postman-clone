using Microsoft.EntityFrameworkCore;
using PostmanClone.CollectionsService.Contracts;
using PostmanClone.CollectionsService.Data;
using PostmanClone.CollectionsService.Domain;
using PostmanClone.ServiceDefaults.Paging;

namespace PostmanClone.CollectionsService.Services;

/// <summary>
/// Collections of one workspace (CONTRACTS §2). Every method reads the workspace from the <see cref="Caller"/> and
/// answers <c>404</c> for a row of another workspace, exactly as for a row that does not exist.
/// </summary>
public interface ICollectionService
{
    Task<PageResult<CollectionSummary>> ListAsync(Caller caller, string? query, PageRequest page, CancellationToken cancellationToken);

    Task<Outcome<CollectionDto>> CreateAsync(Caller caller, CreateCollectionRequest request, CancellationToken cancellationToken);

    Task<Outcome<CollectionTree>> GetTreeAsync(Caller caller, Guid id, CancellationToken cancellationToken);

    Task<Outcome<CollectionDto>> UpdateAsync(Caller caller, Guid id, UpdateCollectionRequest request, CancellationToken cancellationToken);

    Task<Outcome> DeleteAsync(Caller caller, Guid id, CancellationToken cancellationToken);
}

public sealed class CollectionService(CollectionsDbContext db, TimeProvider clock) : ICollectionService
{
    private const string ChangedElsewhere = "The collection was changed by another request. Reload it and try again.";

    public async Task<PageResult<CollectionSummary>> ListAsync(Caller caller, string? query, PageRequest page, CancellationToken cancellationToken)
    {
        var collections = db.Collections.AsNoTracking().Where(collection => collection.WorkspaceId == caller.WorkspaceId);

        var term = query?.Trim();
        if (!string.IsNullOrEmpty(term))
        {
            // Case-insensitive, and the user's % and _ stay literal (LikePattern). The request count is a correlated
            // subquery, so the page comes back in one statement, not one query per collection.
            var pattern = $"%{LikePattern.Escape(term.ToLowerInvariant())}%";
            collections = collections.Where(collection =>
                EF.Functions.Like(collection.Name.ToLower(), pattern, LikePattern.EscapeCharacter));
        }

        var total = await collections.CountAsync(cancellationToken);
        var items = await collections
            .OrderBy(collection => collection.Name)
            .ThenBy(collection => collection.Id)
            .Skip((int)Math.Min(page.Skip, int.MaxValue))
            .Take(page.PageSize)
            .Select(collection => new CollectionSummary(
                collection.Id,
                collection.Name,
                collection.Description,
                collection.CreatedBy,
                collection.CreatedAt,
                collection.UpdatedAt,
                db.SavedRequests.Count(request => request.CollectionId == collection.Id)))
            .ToListAsync(cancellationToken);

        return new PageResult<CollectionSummary>(items, page.Page, page.PageSize, total);
    }

    public async Task<Outcome<CollectionDto>> CreateAsync(Caller caller, CreateCollectionRequest request, CancellationToken cancellationToken)
    {
        return await CollectionsTransactions.RunAsync<Outcome<CollectionDto>>(db, async token =>
        {
            await CollectionsTransactions.LockWorkspaceAsync(db, caller.WorkspaceId, token);

            var existing = await db.Collections.CountAsync(collection => collection.WorkspaceId == caller.WorkspaceId, token);
            if (existing >= CollectionLimits.MaxCollectionsPerWorkspace)
            {
                return Failures.LimitReached(
                    $"This workspace already holds the maximum of {CollectionLimits.MaxCollectionsPerWorkspace} collections.");
            }

            var now = clock.GetUtcNow();
            var collection = new Collection
            {
                Id = Guid.NewGuid(),
                WorkspaceId = caller.WorkspaceId,
                Name = request.Name.Trim(),
                Description = request.Description,
                CreatedBy = caller.UserId,
                CreatedAt = now,
                UpdatedAt = now,
                Version = 1,
            };

            db.Collections.Add(collection);
            await db.SaveChangesAsync(token);
            return Outcome<CollectionDto>.Ok(Mapping.ToDto(collection));
        }, cancellationToken);
    }

    public async Task<Outcome<CollectionTree>> GetTreeAsync(Caller caller, Guid id, CancellationToken cancellationToken)
    {
        var collection = await db.Collections.AsNoTracking()
            .FirstOrDefaultAsync(row => row.Id == id && row.WorkspaceId == caller.WorkspaceId, cancellationToken);
        if (collection is null)
        {
            return Failures.NotFound();
        }

        var folders = await db.Folders.AsNoTracking()
            .Where(folder => folder.WorkspaceId == caller.WorkspaceId && folder.CollectionId == id)
            .OrderBy(folder => folder.SortOrder)
            .ThenBy(folder => folder.Id)
            .ToListAsync(cancellationToken);

        var requests = await db.SavedRequests.AsNoTracking()
            .Where(request => request.WorkspaceId == caller.WorkspaceId && request.CollectionId == id)
            .OrderBy(request => request.FolderId)
            .ThenBy(request => request.SortOrder)
            .ThenBy(request => request.Id)
            .ToListAsync(cancellationToken);

        return Outcome<CollectionTree>.Ok(new CollectionTree(
            Mapping.ToDto(collection),
            folders.Select(Mapping.ToDto).ToList(),
            requests.Select(Mapping.ToDto).ToList()));
    }

    public async Task<Outcome<CollectionDto>> UpdateAsync(Caller caller, Guid id, UpdateCollectionRequest request, CancellationToken cancellationToken)
    {
        var collection = await db.Collections
            .FirstOrDefaultAsync(row => row.Id == id && row.WorkspaceId == caller.WorkspaceId, cancellationToken);
        if (collection is null)
        {
            return Failures.NotFound();
        }

        collection.Name = request.Name.Trim();
        collection.Description = request.Description;
        collection.UpdatedAt = clock.GetUtcNow();
        collection.Version++;

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Failures.Conflict(ChangedElsewhere);
        }

        return Outcome<CollectionDto>.Ok(Mapping.ToDto(collection));
    }

    public async Task<Outcome> DeleteAsync(Caller caller, Guid id, CancellationToken cancellationToken)
    {
        // The 404 check comes first: a row of another workspace is absent, so the role is never revealed to it.
        var collection = await db.Collections
            .FirstOrDefaultAsync(row => row.Id == id && row.WorkspaceId == caller.WorkspaceId, cancellationToken);
        if (collection is null)
        {
            return Failures.NotFound();
        }

        if (!caller.MayDelete(collection))
        {
            return Failures.Forbidden("Only the creator, an Admin or an Owner can delete this collection.");
        }

        // The folders and saved requests go with it: the foreign keys cascade on PostgreSQL (CollectionsDbContext).
        db.Collections.Remove(collection);

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Failures.NotFound();
        }

        return Outcome.Success;
    }
}
