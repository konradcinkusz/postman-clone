using System.Security.Claims;
using PostmanClone.CollectionsService.Services;

namespace PostmanClone.CollectionsService.Api;

/// <summary>
/// The Postman routes of CONTRACTS §2: the import of a file (<c>POST /import/postman</c>) and the export of one collection
/// (<c>GET /collections/{id}/export/postman</c>). Both sit in the <c>/api/v1</c> group, so both need one active workspace.
/// </summary>
public static class PostmanIoEndpoints
{
    public static RouteGroupBuilder MapPostmanIoEndpoints(this RouteGroupBuilder api)
    {
        api.MapPost("/import/postman", async (HttpRequest http, ClaimsPrincipal user, IPostmanIoService service, CancellationToken ct) =>
        {
            var upload = await PostmanUpload.ReadAsync(http, ct);
            if (upload.Failure is { } refusal)
            {
                return Responses.Problem(refusal);
            }

            return Responses.From(
                await service.ImportAsync(Caller.From(user), upload.Value!, ct),
                result => Results.Created($"/api/v1/collections/{result.CollectionId}", result));
        });

        api.MapGet("/collections/{id:guid}/export/postman", async (Guid id, ClaimsPrincipal user, IPostmanIoService service, CancellationToken ct) =>
            Responses.From(
                await service.ExportAsync(Caller.From(user), id, ct),
                file => Results.File(file.Content, "application/json", file.FileName)));

        return api;
    }
}
