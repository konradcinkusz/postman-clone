using Microsoft.EntityFrameworkCore;
using PostmanClone.CollectionsService.Contracts;
using PostmanClone.CollectionsService.Data;
using PostmanClone.CollectionsService.Interop.Curl;
using PostmanClone.ServiceDefaults.Problems;

namespace PostmanClone.CollectionsService.Services;

/// <summary>
/// Imports a cURL command as a saved request (CONTRACTS §2, WP17). The target collection and folder are checked first, so a
/// foreign one is <c>404</c> whatever the command says. The command is then read by <see cref="CurlParser"/> (nothing is
/// executed) and the request is written through <see cref="IRequestService"/>, which runs the same validator and limits as
/// every other write.
/// </summary>
public interface ICurlImportService
{
    Task<Outcome<ImportResult>> ImportAsync(Caller caller, CurlImportBody body, CancellationToken cancellationToken);
}

public sealed class CurlImportService(CollectionsDbContext db, IRequestService requests) : ICurlImportService
{
    private const string InvalidCode = "import_invalid";

    public async Task<Outcome<ImportResult>> ImportAsync(Caller caller, CurlImportBody body, CancellationToken cancellationToken)
    {
        var collection = await db.Collections.AsNoTracking()
            .Where(row => row.Id == body.CollectionId && row.WorkspaceId == caller.WorkspaceId)
            .Select(row => new { row.Id, row.Name })
            .FirstOrDefaultAsync(cancellationToken);
        if (collection is null)
        {
            return Failures.NotFound();
        }

        if (body.FolderId is { } folderId && await FolderRefusalAsync(caller, collection.Id, folderId, cancellationToken) is { } refusal)
        {
            return refusal;
        }

        var parsed = CurlParser.Parse(body.Command);
        if (parsed.Import is not { } import)
        {
            return new Failure(StatusCodes.Status400BadRequest, InvalidCode, parsed.Error ?? "The command could not be read.");
        }

        var created = await requests.CreateAsync(
            caller,
            collection.Id,
            new CreateRequestBody(import.Name, body.FolderId, null, import.Request),
            cancellationToken);
        if (created.Failure is { } failure)
        {
            return failure;
        }

        return Outcome<ImportResult>.Ok(new ImportResult(collection.Id, collection.Name, 0, 1, import.Warnings));
    }

    /// <summary>The folder must exist in the caller's workspace (404 otherwise) and belong to the target collection (400, named by field).</summary>
    private async Task<Failure?> FolderRefusalAsync(Caller caller, Guid collectionId, Guid folderId, CancellationToken cancellationToken)
    {
        var owningCollection = await db.Folders.AsNoTracking()
            .Where(row => row.Id == folderId && row.WorkspaceId == caller.WorkspaceId)
            .Select(row => (Guid?)row.CollectionId)
            .FirstOrDefaultAsync(cancellationToken);
        if (owningCollection is null)
        {
            return Failures.NotFound();
        }

        return owningCollection == collectionId
            ? null
            : Failures.Invalid("folderId", "The folder belongs to another collection.");
    }
}
