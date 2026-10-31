using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PostmanClone.CollectionsService.Contracts;
using PostmanClone.CollectionsService.Data;
using PostmanClone.CollectionsService.Domain;

namespace PostmanClone.CollectionsService.Services;

/// <summary>
/// Saved requests (CONTRACTS §2). Every method reads the workspace from the <see cref="Caller"/> and answers <c>404</c>
/// for a row of another workspace, a missing row, or a target (collection or folder) of another workspace. A request
/// sits in one folder, or at the top of its collection, and its sort order is its place among those siblings.
/// </summary>
public interface IRequestService
{
    Task<Outcome<SavedRequestDto>> CreateAsync(Caller caller, Guid collectionId, CreateRequestBody body, CancellationToken cancellationToken);

    Task<Outcome<SavedRequestDto>> GetAsync(Caller caller, Guid id, CancellationToken cancellationToken);

    /// <param name="expectedVersion">The version from <c>If-Match</c>, or null when the client sent no precondition.</param>
    Task<Outcome<SavedRequestDto>> UpdateAsync(Caller caller, Guid id, int? expectedVersion, UpdateRequestBody body, CancellationToken cancellationToken);

    Task<Outcome<SavedRequestDto>> DuplicateAsync(Caller caller, Guid id, CancellationToken cancellationToken);

    Task<Outcome<SavedRequestDto>> MoveAsync(Caller caller, Guid id, MoveRequestBody body, CancellationToken cancellationToken);

    Task<Outcome> DeleteAsync(Caller caller, Guid id, CancellationToken cancellationToken);
}

public sealed class RequestService(CollectionsDbContext db, TimeProvider clock) : IRequestService
{
    private const string ChangedElsewhere = "The request was changed by another request. Reload it and try again.";

    private const string CopySuffix = " copy";

    private const int MaxNameLength = 200;

    public async Task<Outcome<SavedRequestDto>> CreateAsync(Caller caller, Guid collectionId, CreateRequestBody body, CancellationToken cancellationToken)
    {
        return await CollectionsTransactions.RunAsync<Outcome<SavedRequestDto>>(db, async token =>
        {
            // The collection is checked before the body, as for every id endpoint: a foreign collection is 404 whatever the body says.
            if (!await OwnsCollectionAsync(caller, collectionId, token))
            {
                return Failures.NotFound();
            }

            var accepted = Accept(body.Request);
            if (accepted.Failure is { } refusal)
            {
                return refusal;
            }

            var definition = accepted.Value!;
            await CollectionsTransactions.LockCollectionAsync(db, collectionId, token);

            if (await db.SavedRequests.CountAsync(request => request.CollectionId == collectionId, token) >= CollectionLimits.MaxRequestsPerCollection)
            {
                return Failures.LimitReached(
                    $"This collection already holds the maximum of {CollectionLimits.MaxRequestsPerCollection} requests.");
            }

            if (body.FolderId is { } folderId && await FolderRefusalAsync(caller, collectionId, folderId, token) is { } folderRefusal)
            {
                return folderRefusal;
            }

            var now = clock.GetUtcNow();
            var request = new SavedRequest
            {
                Id = Guid.NewGuid(),
                WorkspaceId = caller.WorkspaceId,
                CollectionId = collectionId,
                FolderId = body.FolderId,
                Name = body.Name.Trim(),
                Description = body.Description,
                Method = definition.Method,
                Definition = Document(definition),
                SortOrder = await NextSortOrderAsync(collectionId, body.FolderId, token),
                CreatedBy = caller.UserId,
                CreatedAt = now,
                UpdatedAt = now,
                Version = 1,
            };

            db.SavedRequests.Add(request);
            await db.SaveChangesAsync(token);
            return Outcome<SavedRequestDto>.Ok(Mapping.ToRequestDto(request));
        }, cancellationToken);
    }

    public async Task<Outcome<SavedRequestDto>> GetAsync(Caller caller, Guid id, CancellationToken cancellationToken)
    {
        var request = await db.SavedRequests.AsNoTracking()
            .FirstOrDefaultAsync(row => row.Id == id && row.WorkspaceId == caller.WorkspaceId, cancellationToken);
        if (request is null)
        {
            return Failures.NotFound();
        }

        return Outcome<SavedRequestDto>.Ok(Mapping.ToRequestDto(request));
    }

    public async Task<Outcome<SavedRequestDto>> UpdateAsync(Caller caller, Guid id, int? expectedVersion, UpdateRequestBody body, CancellationToken cancellationToken)
    {
        var request = await db.SavedRequests
            .FirstOrDefaultAsync(row => row.Id == id && row.WorkspaceId == caller.WorkspaceId, cancellationToken);
        if (request is null)
        {
            return Failures.NotFound();
        }

        // The precondition is checked before anything is written, so a stale client leaves the stored request as it was.
        if (expectedVersion is { } expected && expected != request.Version)
        {
            return Failures.Conflict(ChangedElsewhere);
        }

        var accepted = Accept(body.Request);
        if (accepted.Failure is { } refusal)
        {
            return refusal;
        }

        var definition = accepted.Value!;
        request.Name = body.Name.Trim();
        request.Description = body.Description;
        request.Method = definition.Method;
        request.Definition = Document(definition);
        request.UpdatedAt = clock.GetUtcNow();
        request.Version++;

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Failures.Conflict(ChangedElsewhere);
        }

        return Outcome<SavedRequestDto>.Ok(Mapping.ToRequestDto(request));
    }

    public async Task<Outcome<SavedRequestDto>> DuplicateAsync(Caller caller, Guid id, CancellationToken cancellationToken)
    {
        var collectionId = await CollectionOfRequestAsync(caller, id, cancellationToken);
        if (collectionId is null)
        {
            return Failures.NotFound();
        }

        return await CollectionsTransactions.RunAsync<Outcome<SavedRequestDto>>(db, async token =>
        {
            await CollectionsTransactions.LockCollectionAsync(db, collectionId.Value, token);

            // Read after the lock, so the copy is of the request as it is once the collection's writers have finished.
            var source = await db.SavedRequests.AsNoTracking()
                .FirstOrDefaultAsync(row => row.Id == id && row.WorkspaceId == caller.WorkspaceId, token);
            if (source is null)
            {
                return Failures.NotFound();
            }

            if (await db.SavedRequests.CountAsync(request => request.CollectionId == source.CollectionId, token) >= CollectionLimits.MaxRequestsPerCollection)
            {
                return Failures.LimitReached(
                    $"This collection already holds the maximum of {CollectionLimits.MaxRequestsPerCollection} requests.");
            }

            var now = clock.GetUtcNow();
            var copy = new SavedRequest
            {
                Id = Guid.NewGuid(),
                WorkspaceId = caller.WorkspaceId,
                CollectionId = source.CollectionId,
                FolderId = source.FolderId,
                Name = CopyName(source.Name),
                Description = source.Description,
                Method = source.Method,
                Definition = JsonDocument.Parse(source.Definition.RootElement.GetRawText()),
                SortOrder = await NextSortOrderAsync(source.CollectionId, source.FolderId, token),
                CreatedBy = caller.UserId,
                CreatedAt = now,
                UpdatedAt = now,
                Version = 1,
            };

            db.SavedRequests.Add(copy);
            await db.SaveChangesAsync(token);
            return Outcome<SavedRequestDto>.Ok(Mapping.ToRequestDto(copy));
        }, cancellationToken);
    }

    public async Task<Outcome<SavedRequestDto>> MoveAsync(Caller caller, Guid id, MoveRequestBody body, CancellationToken cancellationToken)
    {
        var sourceCollection = await CollectionOfRequestAsync(caller, id, cancellationToken);
        if (sourceCollection is null)
        {
            return Failures.NotFound();
        }

        var source = sourceCollection.Value;
        var target = body.CollectionId ?? source;

        return await CollectionsTransactions.RunAsync<Outcome<SavedRequestDto>>(db, async token =>
        {
            // A target collection of another workspace is absent for this caller: 404, before anything is locked.
            if (!await OwnsCollectionAsync(caller, target, token))
            {
                return Failures.NotFound();
            }

            if (body.FolderId is { } folderId && await FolderRefusalAsync(caller, target, folderId, token) is { } folderRefusal)
            {
                return folderRefusal;
            }

            // Both collections are locked in one fixed order, so two moves in opposite directions cannot deadlock.
            foreach (var collection in new[] { source, target }.Distinct().Order())
            {
                await CollectionsTransactions.LockCollectionAsync(db, collection, token);
            }

            var request = await db.SavedRequests
                .FirstOrDefaultAsync(row => row.Id == id && row.WorkspaceId == caller.WorkspaceId, token);
            if (request is null)
            {
                return Failures.NotFound();
            }

            // The request was moved by someone else after the first read: its lock was taken on the wrong collection.
            if (request.CollectionId != source)
            {
                return Failures.Conflict(ChangedElsewhere);
            }

            var sameCollection = target == source;
            if (!sameCollection && await db.SavedRequests.CountAsync(row => row.CollectionId == target, token) >= CollectionLimits.MaxRequestsPerCollection)
            {
                return Failures.LimitReached(
                    $"This collection already holds the maximum of {CollectionLimits.MaxRequestsPerCollection} requests.");
            }

            var targetFolder = body.FolderId;
            var sameSlot = sameCollection && request.FolderId == targetFolder;

            // Both lists are read before anything changes. Within one slot the request is in its own list, at its place.
            var oldSiblings = sameSlot
                ? new List<SavedRequest>()
                : (await SiblingsAsync(source, request.FolderId, token)).Where(row => row.Id != request.Id).ToList();
            var targetSiblings = await SiblingsAsync(target, targetFolder, token);
            var others = targetSiblings.Where(row => row.Id != request.Id).ToList();

            var index = body.SortOrder is { } requested
                ? Math.Min(requested, others.Count)
                : sameSlot ? targetSiblings.IndexOf(request) : others.Count;

            others.Insert(index, request);
            if (!sameSlot)
            {
                request.CollectionId = target;
                request.FolderId = targetFolder;
                request.UpdatedAt = clock.GetUtcNow();
                request.Version++;

                // The gap the request leaves behind closes, so the siblings it left keep the orders 0, 1, 2 ...
                Renumber(oldSiblings);
            }

            Renumber(others);

            try
            {
                await db.SaveChangesAsync(token);
            }
            catch (DbUpdateConcurrencyException)
            {
                return Failures.Conflict(ChangedElsewhere);
            }

            return Outcome<SavedRequestDto>.Ok(Mapping.ToRequestDto(request));
        }, cancellationToken);
    }

    public async Task<Outcome> DeleteAsync(Caller caller, Guid id, CancellationToken cancellationToken)
    {
        var collectionId = await CollectionOfRequestAsync(caller, id, cancellationToken);
        if (collectionId is null)
        {
            return Failures.NotFound();
        }

        return await CollectionsTransactions.RunAsync<Outcome>(db, async token =>
        {
            await CollectionsTransactions.LockCollectionAsync(db, collectionId.Value, token);

            var request = await db.SavedRequests
                .FirstOrDefaultAsync(row => row.Id == id && row.WorkspaceId == caller.WorkspaceId, token);
            if (request is null)
            {
                return Failures.NotFound();
            }

            var remaining = (await SiblingsAsync(request.CollectionId, request.FolderId, token))
                .Where(row => row.Id != request.Id)
                .ToList();
            db.SavedRequests.Remove(request);
            Renumber(remaining);

            try
            {
                await db.SaveChangesAsync(token);
            }
            catch (DbUpdateConcurrencyException)
            {
                return Failures.NotFound();
            }

            return Outcome.Success;
        }, cancellationToken);
    }

    /// <summary>The collection a request belongs to, or null when the request is absent or of another workspace.</summary>
    private Task<Guid?> CollectionOfRequestAsync(Caller caller, Guid requestId, CancellationToken cancellationToken) =>
        db.SavedRequests.AsNoTracking()
            .Where(request => request.Id == requestId && request.WorkspaceId == caller.WorkspaceId)
            .Select(request => (Guid?)request.CollectionId)
            .FirstOrDefaultAsync(cancellationToken);

    private Task<bool> OwnsCollectionAsync(Caller caller, Guid collectionId, CancellationToken cancellationToken) =>
        db.Collections.AnyAsync(collection => collection.Id == collectionId && collection.WorkspaceId == caller.WorkspaceId, cancellationToken);

    /// <summary>
    /// The folder a request is placed in must exist in the caller's workspace (404 otherwise) and belong to the target
    /// collection (400, named by field). A folder of another collection is not a row the caller may name, so it is refused.
    /// </summary>
    private async Task<Failure?> FolderRefusalAsync(Caller caller, Guid collectionId, Guid folderId, CancellationToken cancellationToken)
    {
        var owningCollection = await db.Folders.AsNoTracking()
            .Where(folder => folder.Id == folderId && folder.WorkspaceId == caller.WorkspaceId)
            .Select(folder => (Guid?)folder.CollectionId)
            .FirstOrDefaultAsync(cancellationToken);

        if (owningCollection is null)
        {
            return Failures.NotFound();
        }

        return owningCollection == collectionId
            ? null
            : Failures.Invalid("folderId", "The folder belongs to another collection.");
    }

    /// <summary>The requests of one slot (a folder or the top of a collection) in sort order, tracked so the writes are saved.</summary>
    private Task<List<SavedRequest>> SiblingsAsync(Guid collectionId, Guid? folderId, CancellationToken cancellationToken) =>
        db.SavedRequests
            .Where(request => request.CollectionId == collectionId && request.FolderId == folderId)
            .OrderBy(request => request.SortOrder)
            .ThenBy(request => request.Id)
            .ToListAsync(cancellationToken);

    /// <summary>The sort order that puts a new request after the last one of its slot.</summary>
    private async Task<int> NextSortOrderAsync(Guid collectionId, Guid? folderId, CancellationToken cancellationToken)
    {
        var last = await db.SavedRequests.AsNoTracking()
            .Where(request => request.CollectionId == collectionId && request.FolderId == folderId)
            .Select(request => (int?)request.SortOrder)
            .MaxAsync(cancellationToken);
        return last is null ? 0 : last.Value + 1;
    }

    /// <summary>
    /// Gives a slot's requests, already in order, the sort orders 0, 1, 2 ... Only the sort order changes: a reorder is not
    /// an edit of the request, so it does not move its version and a client's <c>If-Match</c> stays valid.
    /// </summary>
    private static void Renumber(IReadOnlyList<SavedRequest> ordered)
    {
        for (var position = 0; position < ordered.Count; position++)
        {
            ordered[position].SortOrder = position;
        }
    }

    /// <summary>
    /// The name of a duplicate: the original's name and " copy", the original's part shortened when needed so the copy
    /// still fits the 200-character name limit (CONTRACTS §2).
    /// </summary>
    private static string CopyName(string name)
    {
        var stem = name.Length + CopySuffix.Length > MaxNameLength
            ? name[..(MaxNameLength - CopySuffix.Length)]
            : name;
        return stem + CopySuffix;
    }

    /// <summary>Normalizes and checks a definition. The 413 size check runs first, so a huge body is never walked.</summary>
    private static Outcome<RequestDefinition> Accept(RequestDefinition definition)
    {
        var normalized = RequestDefinitionValidator.Normalize(definition);
        if (RequestDefinitionValidator.ExceedsMaxSize(normalized))
        {
            return Failures.PayloadTooLarge();
        }

        var validation = RequestDefinitionValidator.Validate(normalized);
        if (!validation.IsValid)
        {
            return Failures.FromIssues(validation);
        }

        return Outcome<RequestDefinition>.Ok(normalized);
    }

    private static JsonDocument Document(RequestDefinition definition) =>
        JsonSerializer.SerializeToDocument(definition, JsonSerializerOptions.Web);
}
