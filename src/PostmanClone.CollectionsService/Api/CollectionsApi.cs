using Microsoft.AspNetCore.Http;
using PostmanClone.ServiceDefaults;
using PostmanClone.ServiceDefaults.Authentication;
using PostmanClone.ServiceDefaults.RateLimiting;

namespace PostmanClone.CollectionsService.Api;

/// <summary>
/// The routes of CollectionsService. Health is mapped anonymously by the kernel. Every domain route sits in the
/// <c>/api/v1</c> group, which requires exactly one active workspace (CONTRACTS §0) and the <c>api</c> rate limit.
/// WP04 adds no domain routes; the collections endpoints arrive with WP08.
/// </summary>
public static class CollectionsApi
{
    public static WebApplication MapCollectionsApi(this WebApplication app)
    {
        app.MapDefaultEndpoints();

        var api = app.MapGroup("/api/v1")
            .RequireActiveWorkspace()
            .RequireRateLimiting(RateLimitPolicies.Api);

        // Internal probe: answers with the workspace the token names, so the workspace gate and the
        // "workspace comes from the token only" rule are tested through a real route. It is excluded from the
        // API description and carries no data. Remove it when the first domain route lands (WP08).
        api.MapGet("/internal/workspace", (HttpContext http) =>
        {
            var resolution = WorkspaceClaims.Resolve(http.User);
            return Results.Ok(new WorkspaceProbe(resolution.WorkspaceId, resolution.Role));
        })
        .ExcludeFromDescription();

        return app;
    }

    /// <summary>The probe's answer: the workspace and role the token resolved to.</summary>
    public sealed record WorkspaceProbe(Guid? WorkspaceId, string? Role);
}
