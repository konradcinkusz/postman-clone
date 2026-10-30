using Microsoft.EntityFrameworkCore;
using PostmanClone.CollectionsService.Contracts;
using PostmanClone.CollectionsService.Data;
using PostmanClone.CollectionsService.Domain;

namespace PostmanClone.CollectionsService.Services;

/// <summary>
/// Folders of a collection (CONTRACTS §2). A write that changes the shape of a collection's tree takes the collection's
/// lock first and reads the tree after it, so the sort orders it computes are the ones it writes, and two writers never
/// give two folders the same position.
/// </summary>
public interface IFolderService
{
    Task<Outcome<FolderDto>> CreateAsync(Caller caller, Guid collectionId, CreateFolderRequest request, CancellationToken cancellationToken);

    Task<Outcome<FolderDto>> RenameAsync(Caller caller, Guid id, RenameFolderRequest request, CancellationToken cancellationToken);

    Task<Outcome<FolderDto>> MoveAsync(Caller caller, Guid id, MoveFolderRequest request, CancellationToken cancellationToken);

    Task<Outcome> DeleteAsync(Caller caller, Guid id, CancellationToken cancellationToken);
}

public sealed class FolderService(CollectionsDbContext db, TimeProvider clock) : IFolderService
{
    public async Task<Outcome<FolderDto>> CreateAsync(Caller caller, Guid collectionId, CreateFolderRequest request, CancellationToken cancellationToken)
    {
        return await CollectionsTransactions.RunAsync<Outcome<FolderDto>>(db, async token =>
        {
            if (!await db.Collections.AnyAsync(collection => collection.Id == collectionId && collection.WorkspaceId == caller.WorkspaceId, token))
            {
                return Failures.NotFound();
            }

            await CollectionsTransactions.LockCollectionAsync(db, collectionId, token);
            var folders = await LoadFoldersAsync(caller, collectionId, token);
            var tree = new FolderTree(folders);

            if (folders.Count >= CollectionLimits.MaxFoldersPerCollection)
            {
                return Failures.LimitReached(
                    $"This collection already holds the maximum of {CollectionLimits.MaxFoldersPerCollection} folders.");
            }

            var parentId = request.ParentFolderId;
            if (parentId is { } parent)
            {
                if (await ParentRefusalAsync(caller, collectionId, parent, tree, token) is { } refusal)
                {
                    return refusal;
                }

                if (tree.DepthOf(parent) + 1 > CollectionLimits.MaxFolderDepth)
                {
                    return Failures.DepthExceeded();
                }
            }

            // Appending: the new folder sorts after the last sibling, so the siblings keep their order.
            var siblings = tree.ChildrenOf(parentId);
            var now = clock.GetUtcNow();
            var folder = new Folder
            {
                Id = Guid.NewGuid(),
                WorkspaceId = caller.WorkspaceId,
                CollectionId = collectionId,
                ParentFolderId = parentId,
                Name = request.Name.Trim(),
                SortOrder = siblings.Count == 0 ? 0 : siblings[^1].SortOrder + 1,
                CreatedBy = caller.UserId,
                CreatedAt = now,
                UpdatedAt = now,
                Version = 1,
            };

            db.Folders.Add(folder);
            await db.SaveChangesAsync(token);
            return Outcome<FolderDto>.Ok(Mapping.ToDto(folder));
        }, cancellationToken);
    }

    public async Task<Outcome<FolderDto>> RenameAsync(Caller caller, Guid id, RenameFolderRequest request, CancellationToken cancellationToken)
    {
        var folder = await db.Folders
            .FirstOrDefaultAsync(row => row.Id == id && row.WorkspaceId == caller.WorkspaceId, cancellationToken);
        if (folder is null)
        {
            return Failures.NotFound();
        }

        folder.Name = request.Name.Trim();
        folder.UpdatedAt = clock.GetUtcNow();
        folder.Version++;
        await db.SaveChangesAsync(cancellationToken);
        return Outcome<FolderDto>.Ok(Mapping.ToDto(folder));
    }

    public async Task<Outcome<FolderDto>> MoveAsync(Caller caller, Guid id, MoveFolderRequest request, CancellationToken cancellationToken)
    {
        return await CollectionsTransactions.RunAsync<Outcome<FolderDto>>(db, async token =>
        {
            var collectionId = await OwnedCollectionOfFolderAsync(caller, id, token);
            if (collectionId is null)
            {
                return Failures.NotFound();
            }

            await CollectionsTransactions.LockCollectionAsync(db, collectionId.Value, token);
            var tree = new FolderTree(await LoadFoldersAsync(caller, collectionId.Value, token));
            var folder = tree.Find(id);
            if (folder is null)
            {
                return Failures.NotFound();
            }

            var oldParentId = folder.ParentFolderId;
            var newParentId = request.ParentFolderId;
            if (newParentId is { } parent && await ParentRefusalAsync(caller, collectionId.Value, parent, tree, token) is { } refusal)
            {
                return refusal;
            }

            if (tree.WouldCycle(folder.Id, newParentId))
            {
                return Failures.CycleDetected();
            }

            // The whole moved subtree must still fit: its deepest folder sits below the new parent.
            if (tree.DepthOf(newParentId) + tree.HeightOf(folder.Id) > CollectionLimits.MaxFolderDepth)
            {
                return Failures.DepthExceeded();
            }

            var now = clock.GetUtcNow();
            var sameParent = oldParentId == newParentId;
            var ordered = tree.ChildrenOf(newParentId).ToList();
            var others = ordered.Where(sibling => sibling.Id != folder.Id).ToList();

            // Without a requested position, a folder that stays under its parent keeps its place and one that changes
            // parent goes last. A requested position is clamped to the end of the list.
            var index = request.SortOrder is { } requested
                ? Math.Min(requested, others.Count)
                : sameParent ? ordered.IndexOf(folder) : others.Count;

            others.Insert(index, folder);
            folder.ParentFolderId = newParentId;
            folder.UpdatedAt = now;
            folder.Version++;
            Renumber(others, now);

            if (!sameParent)
            {
                // The old parent's remaining folders close the gap the moved folder left.
                Renumber(tree.ChildrenOf(oldParentId).Where(sibling => sibling.Id != folder.Id).ToList(), now);
            }

            await db.SaveChangesAsync(token);
            return Outcome<FolderDto>.Ok(Mapping.ToDto(folder));
        }, cancellationToken);
    }

    public async Task<Outcome> DeleteAsync(Caller caller, Guid id, CancellationToken cancellationToken)
    {
        return await CollectionsTransactions.RunAsync<Outcome>(db, async token =>
        {
            var collectionId = await OwnedCollectionOfFolderAsync(caller, id, token);
            if (collectionId is null)
            {
                return Failures.NotFound();
            }

            await CollectionsTransactions.LockCollectionAsync(db, collectionId.Value, token);
            var tree = new FolderTree(await LoadFoldersAsync(caller, collectionId.Value, token));
            var folder = tree.Find(id);
            if (folder is null)
            {
                return Failures.NotFound();
            }

            // The folder's subfolders are loaded with the tree, so EF removes them with it; the saved requests under it
            // go with the foreign-key cascade. The siblings left behind are renumbered in the same transaction.
            var remaining = tree.ChildrenOf(folder.ParentFolderId).Where(sibling => sibling.Id != folder.Id).ToList();
            db.Folders.Remove(folder);
            Renumber(remaining, clock.GetUtcNow());
            await db.SaveChangesAsync(token);
            return Outcome.Success;
        }, cancellationToken);
    }

    /// <summary>The collection a folder's tree belongs to, or null when the folder is absent or of another workspace.</summary>
    private Task<Guid?> OwnedCollectionOfFolderAsync(Caller caller, Guid folderId, CancellationToken cancellationToken) =>
        db.Folders.AsNoTracking()
            .Where(folder => folder.Id == folderId && folder.WorkspaceId == caller.WorkspaceId)
            .Select(folder => (Guid?)folder.CollectionId)
            .FirstOrDefaultAsync(cancellationToken);

    /// <summary>
    /// The folders of one collection, tracked so the writes made through them are saved. Loaded after the lock, so
    /// nothing read here is older than the last committed write to the tree.
    /// </summary>
    private Task<List<Folder>> LoadFoldersAsync(Caller caller, Guid collectionId, CancellationToken cancellationToken) =>
        db.Folders
            .Where(folder => folder.WorkspaceId == caller.WorkspaceId && folder.CollectionId == collectionId)
            .ToListAsync(cancellationToken);

    /// <summary>
    /// Why a parent folder cannot be used, or null when it can. A parent of another workspace, or none at all, is
    /// <c>404</c>; one in another collection of the caller's own workspace is a bad request, named by field.
    /// </summary>
    private async Task<Failure?> ParentRefusalAsync(Caller caller, Guid collectionId, Guid parentId, FolderTree tree, CancellationToken cancellationToken)
    {
        var owningCollection = await OwnedCollectionOfFolderAsync(caller, parentId, cancellationToken);
        if (owningCollection is null)
        {
            return Failures.NotFound();
        }

        if (owningCollection != collectionId)
        {
            return Failures.Invalid("parentFolderId", "The parent folder belongs to another collection.");
        }

        // Found in the database but not in the tree: it was removed after the lock was taken.
        return tree.Find(parentId) is null ? Failures.NotFound() : null;
    }

    /// <summary>Gives a list of siblings, already in order, the sort orders 0, 1, 2 … and stamps only the rows it changed.</summary>
    private static void Renumber(IReadOnlyList<Folder> siblings, DateTimeOffset now)
    {
        for (var position = 0; position < siblings.Count; position++)
        {
            var sibling = siblings[position];
            if (sibling.SortOrder == position)
            {
                continue;
            }

            sibling.SortOrder = position;
            sibling.UpdatedAt = now;
            sibling.Version++;
        }
    }
}
