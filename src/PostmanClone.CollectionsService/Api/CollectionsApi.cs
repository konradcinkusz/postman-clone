using PostmanClone.ServiceDefaults.Authentication;
using PostmanClone.ServiceDefaults.RateLimiting;

namespace PostmanClone.CollectionsService.Api;

/// <summary>
/// The routes of CollectionsService. Health is mapped anonymously by the kernel (Program.cs). Every domain route sits in the
/// <c>/api/v1</c> group, which requires a signed-in user and exactly one active workspace (CONTRACTS §0) and the <c>api</c> rate limit.
/// The route list is served by OpenAPI in Development only; production answers 404 for it.
/// </summary>
public static class CollectionsApi
{
    public static WebApplication MapCollectionsApi(this WebApplication app)
    {
        var api = app.MapGroup("/api/v1")
            .RequireAuthorization()
            .RequireActiveWorkspace()
            .RequireRateLimiting(RateLimitPolicies.Api);

        api.MapCollectionEndpoints();
        api.MapFolderEndpoints();
        api.MapRequestEndpoints();
        api.MapPostmanIoEndpoints();
        api.MapCurlEndpoints();

        if (app.Environment.IsDevelopment())
        {
            app.MapOpenApi();
        }

        return app;
    }
}
