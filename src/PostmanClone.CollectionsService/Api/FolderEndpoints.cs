using System.Security.Claims;
using PostmanClone.CollectionsService.Contracts;
using PostmanClone.CollectionsService.Services;
using PostmanClone.ServiceDefaults.Validation;

namespace PostmanClone.CollectionsService.Api;

/// <summary>The folder routes of CONTRACTS §2. A folder is created inside a collection, then renamed, moved or deleted by its id.</summary>
public static class FolderEndpoints
{
    public static RouteGroupBuilder MapFolderEndpoints(this RouteGroupBuilder api)
    {
        api.MapPost("/collections/{id:guid}/folders", async (Guid id, CreateFolderRequest request, ClaimsPrincipal user, IFolderService service, CancellationToken ct) =>
                Responses.From(await service.CreateAsync(Caller.From(user), id, request, ct),
                    folder => Results.Created($"/api/v1/folders/{folder.Id}", folder)))
            .ValidateDataAnnotations();

        var folders = api.MapGroup("/folders");

        folders.MapPut("{id:guid}", async (Guid id, RenameFolderRequest request, ClaimsPrincipal user, IFolderService service, CancellationToken ct) =>
                Responses.From(await service.RenameAsync(Caller.From(user), id, request, ct), folder => Results.Ok(folder)))
            .ValidateDataAnnotations();

        folders.MapPost("{id:guid}/move", async (Guid id, MoveFolderRequest request, ClaimsPrincipal user, IFolderService service, CancellationToken ct) =>
                Responses.From(await service.MoveAsync(Caller.From(user), id, request, ct), folder => Results.Ok(folder)))
            .ValidateDataAnnotations();

        folders.MapDelete("{id:guid}", async (Guid id, ClaimsPrincipal user, IFolderService service, CancellationToken ct) =>
            Responses.From(await service.DeleteAsync(Caller.From(user), id, ct)));

        return api;
    }
}
