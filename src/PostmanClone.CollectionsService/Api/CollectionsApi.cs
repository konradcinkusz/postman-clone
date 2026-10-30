using PostmanClone.ServiceDefaults;
using PostmanClone.ServiceDefaults.Authentication;
using PostmanClone.ServiceDefaults.RateLimiting;

namespace PostmanClone.CollectionsService.Api;

/// <summary>
/// The routes of CollectionsService. Health is mapped anonymously by the kernel. Every domain route sits in the
/// <c>/api/v1</c> group, which requires exactly one active workspace (CONTRACTS §0) and the <c>api</c> rate limit.
/// The route list is served by OpenAPI in Development only; production answers 404 for it.
/// </summary>
public static class CollectionsApi
{
    public static WebApplication MapCollectionsApi(this WebApplication app)
    {
        app.MapDefaultEndpoints();

        var api = app.MapGroup("/api/v1")
            .RequireActiveWorkspace()
            .RequireRateLimiting(RateLimitPolicies.Api);

        api.MapCollectionEndpoints();
        api.MapFolderEndpoints();

        if (app.Environment.IsDevelopment())
        {
            app.MapOpenApi();
        }

        return app;
    }
}
