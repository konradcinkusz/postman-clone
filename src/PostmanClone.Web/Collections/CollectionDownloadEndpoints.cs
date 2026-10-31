using PostmanClone.ServiceDefaults.Authentication;
using PostmanClone.Web.Api;
using PostmanClone.Web.Auth;

namespace PostmanClone.Web.Collections;

/// <summary>
/// The Web's download of a collection as a Postman v2.1 file (WP23, CONTRACTS §5: <c>GET /download/collections/{id}</c>, the BFF
/// export). It is a plain minimal endpoint, not a component, so the browser's own download takes the answer and no JavaScript
/// is needed. The session's bearer is attached here, on the server; the browser holds only its session cookie.
/// </summary>
public static class CollectionDownloadEndpoints
{
    /// <summary>The media type of the export (CONTRACTS §2).</summary>
    public const string PostmanMediaType = "application/json";

    /// <summary>The name used when the service names no file.</summary>
    public const string FallbackFileName = "collection.postman_collection.json";

    public static WebApplication MapCollectionDownloadEndpoints(this WebApplication app)
    {
        // The route constraint answers a non-Guid id with 404 before any call is made.
        app.MapGet("/download/collections/{id:guid}", DownloadAsync).RequireAuthorization();
        return app;
    }

    private static async Task<IResult> DownloadAsync(
        Guid id,
        HttpContext http,
        CollectionsClient collections,
        TokenRefresher refresher,
        ILogger<CollectionDownloadLog> logger,
        CancellationToken cancellationToken)
    {
        // A download is a navigation, so the answers that are not files are plain text, and never cached.
        http.Response.Headers.CacheControl = "no-store";
        http.Response.Headers["X-Content-Type-Options"] = "nosniff";

        var sessionId = http.User.FindFirst(ServerSession.SessionIdClaim)?.Value;
        if (sessionId is null)
        {
            return Results.Redirect(ReturnUrlPolicy.SignInPath(CurrentPath(http)));
        }

        // Zero or several organizations have no workspace to export from (CONTRACTS §0: fail closed).
        if (!WorkspaceClaims.Resolve(http.User).IsActive)
        {
            return Results.Redirect(WorkspaceRouting.NoWorkspacePath);
        }

        var accessToken = await refresher.AccessTokenAsync(sessionId, touch: true, cancellationToken);
        if (accessToken is null)
        {
            return Results.Redirect(ReturnUrlPolicy.SignInPath(CurrentPath(http)));
        }

        var download = await collections.OpenPostmanExportAsync(accessToken, id, cancellationToken);
        if (download.Response is not { } response)
        {
            return Failure(download.Outcome, logger, id);
        }

        logger.LogInformation("Export of collection {CollectionId} answered {Status}", id, (int)response.StatusCode);
        return Results.Stream(
            async body =>
            {
                // The upstream answer is disposed when the copy ends, whether it finished or the client went away.
                using (response)
                {
                    await using var upstream = await response.Content.ReadAsStreamAsync(cancellationToken);
                    await upstream.CopyToAsync(body, cancellationToken);
                }
            },
            contentType: PostmanMediaType,
            fileDownloadName: FileNameOf(response, id));
    }

    /// <summary>
    /// The answer for an export that did not come back as a file. The status says what happened, and the body names nothing
    /// from the collection: a foreign or absent id is the same <c>404</c> (CONTRACTS §0).
    /// </summary>
    private static IResult Failure(CollectionOutcome outcome, ILogger logger, Guid id)
    {
        logger.LogInformation("Export of collection {CollectionId} was not served: {Outcome}", id, outcome);
        return outcome switch
        {
            CollectionOutcome.NotFound => Results.Text("That collection is not in this workspace or no longer exists.", "text/plain; charset=utf-8", statusCode: 404),
            CollectionOutcome.Forbidden => Results.Text("You do not have permission to export this collection.", "text/plain; charset=utf-8", statusCode: 403),
            CollectionOutcome.RateLimited => Results.Text("Too many requests in a short time. Wait a moment and try again.", "text/plain; charset=utf-8", statusCode: 429),
            CollectionOutcome.SessionEnded => Results.Redirect(ReturnUrlPolicy.SignInPath(ReturnUrlPolicy.Fallback)),
            CollectionOutcome.NoWorkspace => Results.Redirect(WorkspaceRouting.NoWorkspacePath),
            CollectionOutcome.Unavailable => Results.Text(CollectionMessages.For(CollectionOutcome.Unavailable, null), "text/plain; charset=utf-8", statusCode: 503),
            _ => Results.Text("The export was refused by the collections service.", "text/plain; charset=utf-8", statusCode: 502),
        };
    }

    /// <summary>
    /// The file name the service chose, when it sent a usable one; otherwise the fallback. A name is only a label, but it is
    /// still cut to a safe leaf name before the Web hands it to the browser.
    /// </summary>
    private static string FileNameOf(HttpResponseMessage response, Guid id)
    {
        var name = response.Content.Headers.ContentDisposition?.FileName?.Trim('"');
        var leaf = string.IsNullOrWhiteSpace(name) ? null : ImportRules.SafeFileName(name);
        return leaf is null || leaf == "collection.json" ? $"collection-{id:N}.postman_collection.json" : leaf;
    }

    private static string CurrentPath(HttpContext http) => $"{http.Request.Path}{http.Request.QueryString}";
}

/// <summary>The logger category of the download endpoint. It logs the status and the outcome, never the file's bytes.</summary>
public sealed class CollectionDownloadLog;
