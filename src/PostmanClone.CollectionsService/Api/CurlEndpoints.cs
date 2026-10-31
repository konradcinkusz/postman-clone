using System.Security.Claims;
using PostmanClone.CollectionsService.Contracts;
using PostmanClone.CollectionsService.Services;
using PostmanClone.ServiceDefaults.Validation;

namespace PostmanClone.CollectionsService.Api;

/// <summary>
/// The cURL routes of WP17 (CONTRACTS §2): import a command into a collection, and export a saved request as a command.
/// The command is untrusted text: the import reads it and never runs it (see <c>Interop/Curl</c>).
/// </summary>
public static class CurlEndpoints
{
    public static RouteGroupBuilder MapCurlEndpoints(this RouteGroupBuilder api)
    {
        api.MapPost("/import/curl", async (CurlImportBody body, ClaimsPrincipal user, ICurlImportService service, CancellationToken ct) =>
                Responses.From(await service.ImportAsync(Caller.From(user), body, ct),
                    result => Results.Created($"/api/v1/collections/{result.CollectionId}", result)))
            .ValidateDataAnnotations();

        api.MapGet("/requests/{id:guid}/export/curl", async (Guid id, ClaimsPrincipal user, ICurlExportService service, CancellationToken ct) =>
            Responses.From(await service.ExportAsync(Caller.From(user), id, ct),
                command => Results.Text(command, "text/plain; charset=utf-8")));

        return api;
    }
}
