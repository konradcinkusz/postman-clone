using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PostmanClone.CollectionsService.Contracts;
using PostmanClone.CollectionsService.Data;
using PostmanClone.CollectionsService.Interop.Curl;
using PostmanClone.ServiceDefaults.Problems;

namespace PostmanClone.CollectionsService.Services;

/// <summary>
/// Exports a saved request as a cURL command (CONTRACTS §2, WP17). A request of another workspace, or a missing one, is
/// <c>404</c>. The command keeps every <c>{{variable}}</c> reference as written; no secret value is ever stored to be exported.
/// </summary>
public interface ICurlExportService
{
    Task<Outcome<string>> ExportAsync(Caller caller, Guid requestId, CancellationToken cancellationToken);
}

public sealed class CurlExportService(CollectionsDbContext db) : ICurlExportService
{
    public async Task<Outcome<string>> ExportAsync(Caller caller, Guid requestId, CancellationToken cancellationToken)
    {
        var request = await db.SavedRequests.AsNoTracking()
            .FirstOrDefaultAsync(row => row.Id == requestId && row.WorkspaceId == caller.WorkspaceId, cancellationToken);
        if (request is null)
        {
            return Failures.NotFound();
        }

        var definition = request.Definition.RootElement.Deserialize<RequestDefinition>(JsonSerializerOptions.Web)!;
        return Outcome<string>.Ok(CurlGenerator.Generate(definition));
    }
}
