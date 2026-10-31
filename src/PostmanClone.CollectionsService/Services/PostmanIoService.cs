using Microsoft.EntityFrameworkCore;
using PostmanClone.CollectionsService.Contracts;
using PostmanClone.CollectionsService.Data;
using PostmanClone.CollectionsService.Domain;
using PostmanClone.CollectionsService.Interop.Postman;

namespace PostmanClone.CollectionsService.Services;

/// <summary>
/// Postman import and export of CONTRACTS §2. An import is one transaction: the collection, its folders and its requests
/// are written together or not at all. An export reads one collection of the caller's workspace; another workspace's
/// collection is absent (404).
/// </summary>
public interface IPostmanIoService
{
    Task<Outcome<ImportResult>> ImportAsync(Caller caller, ReadOnlyMemory<byte> file, CancellationToken cancellationToken);

    Task<Outcome<ExportedFile>> ExportAsync(Caller caller, Guid collectionId, CancellationToken cancellationToken);
}

/// <summary>A file to send back: its download name and its bytes.</summary>
public sealed record ExportedFile(string FileName, byte[] Content);

public sealed class PostmanIoService(CollectionsDbContext db, TimeProvider clock) : IPostmanIoService
{
    private const int MaxCollectionNameLength = 100;

    public async Task<Outcome<ImportResult>> ImportAsync(Caller caller, ReadOnlyMemory<byte> file, CancellationToken cancellationToken)
    {
        var parsed = PostmanV2Importer.Parse(file);
        if (parsed.Failure is { } refusal)
        {
            return refusal;
        }

        var plan = parsed.Value!;
        return await CollectionsTransactions.RunAsync<Outcome<ImportResult>>(db, async token =>
        {
            await CollectionsTransactions.LockWorkspaceAsync(db, caller.WorkspaceId, token);

            var collections = await db.Collections.CountAsync(collection => collection.WorkspaceId == caller.WorkspaceId, token);
            if (collections >= CollectionLimits.MaxCollectionsPerWorkspace)
            {
                return Failures.LimitReached(
                    $"This workspace already holds the maximum of {CollectionLimits.MaxCollectionsPerWorkspace} collections.");
            }

            var now = clock.GetUtcNow();
            var collection = new Collection
            {
                Id = Guid.NewGuid(),
                WorkspaceId = caller.WorkspaceId,
                Name = Trim(plan.Name, MaxCollectionNameLength),
                Description = plan.Description,
                CreatedBy = caller.UserId,
                CreatedAt = now,
                UpdatedAt = now,
                Version = 1,
            };
            db.Collections.Add(collection);

            // Folders come in the order the walk met them, so a parent is always written before its children.
            var folderIds = new Dictionary<int, Guid>();
            foreach (var planned in plan.Folders)
            {
                var folder = new Folder
                {
                    Id = Guid.NewGuid(),
                    WorkspaceId = caller.WorkspaceId,
                    CollectionId = collection.Id,
                    ParentFolderId = planned.ParentKey is { } parent ? folderIds[parent] : null,
                    Name = planned.Name,
                    SortOrder = planned.SortOrder,
                    CreatedBy = caller.UserId,
                    CreatedAt = now,
                    UpdatedAt = now,
                    Version = 1,
                };
                folderIds[planned.Key] = folder.Id;
                db.Folders.Add(folder);
            }

            foreach (var planned in plan.Requests)
            {
                db.SavedRequests.Add(new SavedRequest
                {
                    Id = Guid.NewGuid(),
                    WorkspaceId = caller.WorkspaceId,
                    CollectionId = collection.Id,
                    FolderId = planned.FolderKey is { } folder ? folderIds[folder] : null,
                    Name = planned.Name,
                    Description = planned.Description,
                    Method = planned.Definition.Method,
                    Definition = System.Text.Json.JsonSerializer.SerializeToDocument(planned.Definition, System.Text.Json.JsonSerializerOptions.Web),
                    SortOrder = planned.SortOrder,
                    CreatedBy = caller.UserId,
                    CreatedAt = now,
                    UpdatedAt = now,
                    Version = 1,
                });
            }

            await db.SaveChangesAsync(token);
            return Outcome<ImportResult>.Ok(new ImportResult(
                collection.Id,
                collection.Name,
                plan.Folders.Count,
                plan.Requests.Count,
                plan.Warnings));
        }, cancellationToken);
    }

    public async Task<Outcome<ExportedFile>> ExportAsync(Caller caller, Guid collectionId, CancellationToken cancellationToken)
    {
        var collection = await db.Collections.AsNoTracking()
            .FirstOrDefaultAsync(row => row.Id == collectionId && row.WorkspaceId == caller.WorkspaceId, cancellationToken);
        if (collection is null)
        {
            return Failures.NotFound();
        }

        var folders = await db.Folders.AsNoTracking()
            .Where(folder => folder.WorkspaceId == caller.WorkspaceId && folder.CollectionId == collectionId)
            .ToListAsync(cancellationToken);
        var requests = await db.SavedRequests.AsNoTracking()
            .Where(request => request.WorkspaceId == caller.WorkspaceId && request.CollectionId == collectionId)
            .ToListAsync(cancellationToken);

        var content = PostmanV2Exporter.Export(collection, folders, requests);
        return Outcome<ExportedFile>.Ok(new ExportedFile(FileNameOf(collection.Name), content));
    }

    private static string Trim(string name, int maxLength) => name.Length <= maxLength ? name : name[..maxLength];

    /// <summary>The download name: the collection's name reduced to ASCII letters, digits, dashes and underscores.</summary>
    private static string FileNameOf(string name)
    {
        var safe = new string(name.Select(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_' ? character : '-').ToArray()).Trim('-');
        return (safe.Length == 0 ? "collection" : safe) + ".postman_collection.json";
    }
}
