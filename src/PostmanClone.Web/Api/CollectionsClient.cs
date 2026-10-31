using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Net.Http.Headers;
using System.Text.Json;

namespace PostmanClone.Web.Api;

/// <summary>What a CollectionsService call came back with, in the terms the shell acts on.</summary>
public enum CollectionOutcome
{
    /// <summary>A 2xx answer.</summary>
    Succeeded,

    /// <summary>404 <c>not_found</c>: absent, or in another workspace (CONTRACTS §0). The shell says it is gone.</summary>
    NotFound,

    /// <summary>403 <c>forbidden</c>: the caller may not do this (for instance a Member deleting someone else's collection).</summary>
    Forbidden,

    /// <summary>409 <c>conflict</c>: the request changed since it was read (an <c>If-Match</c> mismatch).</summary>
    Conflict,

    /// <summary>400: a rule the service enforces, such as a name's length, a folder's depth or a cycle.</summary>
    Invalid,

    /// <summary>413 <c>payload_too_large</c>.</summary>
    TooLarge,

    /// <summary>429 <c>rate_limited</c>.</summary>
    RateLimited,

    /// <summary>Any other 4xx.</summary>
    Refused,

    /// <summary>No usable answer: a transport failure, a timeout or a 5xx.</summary>
    Unavailable,

    /// <summary>Set by the shell, never by the client: the session has ended, so there is no token to call with.</summary>
    SessionEnded,

    /// <summary>Set by the shell, never by the client: the session has no single active workspace.</summary>
    NoWorkspace,
}

/// <summary>A call that returns nothing on success (a delete). <see cref="Code"/> is the problem's <c>code</c> when refused.</summary>
public sealed record CollectionAnswer(CollectionOutcome Outcome, string? Code);

/// <summary>A call that returns a body on success. <see cref="Code"/> is the problem's <c>code</c> when refused.</summary>
public sealed record CollectionAnswer<T>(CollectionOutcome Outcome, T? Value, string? Code);

/// <summary>
/// A streamed download. On success <see cref="Response"/> is the open answer, and its owner disposes it once the body is copied;
/// on any other outcome it is null.
/// </summary>
public sealed record CollectionDownload(CollectionOutcome Outcome, string? Code, HttpResponseMessage? Response);

/// <summary>
/// The Web's client for CollectionsService (CONTRACTS §2, §5). Each call takes the bearer it needs and passes it through
/// <see cref="AuthenticatedRequest"/>; the client keeps no token. The workspace comes from that token, so no call names one.
/// Failures become outcomes, never exceptions: a down backend must not blank the shell (BLAZOR-WEB-APP §5).
/// </summary>
public sealed class CollectionsClient(IHttpClientFactory clients)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>The largest page the service returns (CONTRACTS §0: pageSize clamped to 1..100).</summary>
    public const int MaximumPageSize = 100;

    public Task<CollectionAnswer<PageResult<CollectionSummary>>> ListCollectionsAsync(string accessToken, int page, int pageSize, CancellationToken cancellationToken) =>
        SendAsync<PageResult<CollectionSummary>>(
            AuthenticatedRequest.Create(HttpMethod.Get, $"/api/v1/collections?page={page}&pageSize={pageSize}", accessToken),
            cancellationToken);

    public Task<CollectionAnswer<CollectionDto>> CreateCollectionAsync(string accessToken, CreateCollectionBody body, CancellationToken cancellationToken) =>
        SendAsync<CollectionDto>(JsonRequest(HttpMethod.Post, "/api/v1/collections", accessToken, body), cancellationToken);

    public Task<CollectionAnswer<CollectionTree>> GetCollectionTreeAsync(string accessToken, Guid collectionId, CancellationToken cancellationToken) =>
        SendAsync<CollectionTree>(AuthenticatedRequest.Create(HttpMethod.Get, $"/api/v1/collections/{collectionId}", accessToken), cancellationToken);

    public Task<CollectionAnswer<CollectionDto>> UpdateCollectionAsync(string accessToken, Guid collectionId, UpdateCollectionBody body, CancellationToken cancellationToken) =>
        SendAsync<CollectionDto>(JsonRequest(HttpMethod.Put, $"/api/v1/collections/{collectionId}", accessToken, body), cancellationToken);

    public Task<CollectionAnswer> DeleteCollectionAsync(string accessToken, Guid collectionId, CancellationToken cancellationToken) =>
        SendEmptyAsync(AuthenticatedRequest.Create(HttpMethod.Delete, $"/api/v1/collections/{collectionId}", accessToken), cancellationToken);

    public Task<CollectionAnswer<FolderDto>> CreateFolderAsync(string accessToken, Guid collectionId, CreateFolderBody body, CancellationToken cancellationToken) =>
        SendAsync<FolderDto>(JsonRequest(HttpMethod.Post, $"/api/v1/collections/{collectionId}/folders", accessToken, body), cancellationToken);

    public Task<CollectionAnswer<FolderDto>> RenameFolderAsync(string accessToken, Guid folderId, RenameFolderBody body, CancellationToken cancellationToken) =>
        SendAsync<FolderDto>(JsonRequest(HttpMethod.Put, $"/api/v1/folders/{folderId}", accessToken, body), cancellationToken);

    public Task<CollectionAnswer> DeleteFolderAsync(string accessToken, Guid folderId, CancellationToken cancellationToken) =>
        SendEmptyAsync(AuthenticatedRequest.Create(HttpMethod.Delete, $"/api/v1/folders/{folderId}", accessToken), cancellationToken);

    public Task<CollectionAnswer<SavedRequestDto>> CreateRequestAsync(string accessToken, Guid collectionId, CreateSavedRequestBody body, CancellationToken cancellationToken) =>
        SendAsync<SavedRequestDto>(JsonRequest(HttpMethod.Post, $"/api/v1/collections/{collectionId}/requests", accessToken, body), cancellationToken);

    public Task<CollectionAnswer<SavedRequestDto>> GetRequestAsync(string accessToken, Guid requestId, CancellationToken cancellationToken) =>
        SendAsync<SavedRequestDto>(AuthenticatedRequest.Create(HttpMethod.Get, $"/api/v1/requests/{requestId}", accessToken), cancellationToken);

    /// <summary>
    /// Replaces a saved request. With <paramref name="expectedVersion"/> the service refuses a write over a newer version
    /// (<c>If-Match</c>, CONTRACTS §2): the answer is then <see cref="CollectionOutcome.Conflict"/>.
    /// </summary>
    public Task<CollectionAnswer<SavedRequestDto>> UpdateRequestAsync(string accessToken, Guid requestId, UpdateSavedRequestBody body, int? expectedVersion, CancellationToken cancellationToken)
    {
        var request = JsonRequest(HttpMethod.Put, $"/api/v1/requests/{requestId}", accessToken, body);
        if (expectedVersion is { } version)
        {
            request.Headers.TryAddWithoutValidation("If-Match", EntityTag(version));
        }

        return SendAsync<SavedRequestDto>(request, cancellationToken);
    }

    public Task<CollectionAnswer<SavedRequestDto>> DuplicateRequestAsync(string accessToken, Guid requestId, CancellationToken cancellationToken) =>
        SendAsync<SavedRequestDto>(AuthenticatedRequest.Create(HttpMethod.Post, $"/api/v1/requests/{requestId}/duplicate", accessToken), cancellationToken);

    public Task<CollectionAnswer<SavedRequestDto>> MoveRequestAsync(string accessToken, Guid requestId, MoveRequestBody body, CancellationToken cancellationToken) =>
        SendAsync<SavedRequestDto>(JsonRequest(HttpMethod.Post, $"/api/v1/requests/{requestId}/move", accessToken, body), cancellationToken);

    public Task<CollectionAnswer> DeleteRequestAsync(string accessToken, Guid requestId, CancellationToken cancellationToken) =>
        SendEmptyAsync(AuthenticatedRequest.Create(HttpMethod.Delete, $"/api/v1/requests/{requestId}", accessToken), cancellationToken);

    /// <summary>
    /// Uploads a Postman v2.1 file as a multipart <c>file</c> part (CONTRACTS §2). The file is streamed from <paramref name="file"/>
    /// into the request: it is never copied into a buffer of the Web. The caller owns the stream and disposes it.
    /// </summary>
    public Task<CollectionAnswer<ImportResult>> ImportPostmanAsync(string accessToken, Stream file, string fileName, CancellationToken cancellationToken)
    {
        var part = new StreamContent(file);
        part.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        var content = new MultipartFormDataContent { { part, "file", fileName } };
        var request = AuthenticatedRequest.Create(HttpMethod.Post, "/api/v1/import/postman", accessToken);
        request.Content = content;
        return SendAsync<ImportResult>(request, cancellationToken);
    }

    public Task<CollectionAnswer<ImportResult>> ImportCurlAsync(string accessToken, CurlImportBody body, CancellationToken cancellationToken) =>
        SendAsync<ImportResult>(JsonRequest(HttpMethod.Post, "/api/v1/import/curl", accessToken, body), cancellationToken);

    /// <summary>The request as a cURL command (CONTRACTS §2): a <c>text/plain</c> body, not JSON.</summary>
    public Task<CollectionAnswer<string>> GetRequestCurlAsync(string accessToken, Guid requestId, CancellationToken cancellationToken) =>
        SendTextAsync(AuthenticatedRequest.Create(HttpMethod.Get, $"/api/v1/requests/{requestId}/export/curl", accessToken), cancellationToken);

    /// <summary>
    /// Opens a collection's Postman export for streaming. Only the headers are read here; the body stays on the wire until the
    /// caller copies it. On success the caller owns <see cref="CollectionDownload.Response"/> and disposes it.
    /// </summary>
    public async Task<CollectionDownload> OpenPostmanExportAsync(string accessToken, Guid collectionId, CancellationToken cancellationToken)
    {
        using var request = AuthenticatedRequest.Create(HttpMethod.Get, $"/api/v1/collections/{collectionId}/export/postman", accessToken);
        var response = await TrySendAsync(request, cancellationToken, HttpCompletionOption.ResponseHeadersRead);
        if (response is null)
        {
            return new CollectionDownload(CollectionOutcome.Unavailable, null, null);
        }

        if (response.IsSuccessStatusCode)
        {
            return new CollectionDownload(CollectionOutcome.Succeeded, null, response);
        }

        using (response)
        {
            return new CollectionDownload(FromStatus(response.StatusCode), await ProblemCodeAsync(response, cancellationToken), null);
        }
    }

    /// <summary>The value CollectionsService sends as <c>ETag</c> and compares on <c>If-Match</c> (EntityTags.Format).</summary>
    public static string EntityTag(int version) => $"\"{version.ToString(CultureInfo.InvariantCulture)}\"";

    private static HttpRequestMessage JsonRequest<T>(HttpMethod method, string path, string accessToken, T body)
    {
        var request = AuthenticatedRequest.Create(method, path, accessToken);
        request.Content = JsonContent.Create(body, options: Json);
        return request;
    }

    private async Task<CollectionAnswer<T>> SendAsync<T>(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        using (request)
        {
            using var response = await TrySendAsync(request, cancellationToken);
            if (response is null)
            {
                return new CollectionAnswer<T>(CollectionOutcome.Unavailable, default, null);
            }

            if (!response.IsSuccessStatusCode)
            {
                return new CollectionAnswer<T>(FromStatus(response.StatusCode), default, await ProblemCodeAsync(response, cancellationToken));
            }

            try
            {
                var value = await response.Content.ReadFromJsonAsync<T>(Json, cancellationToken);
                return value is null
                    ? new CollectionAnswer<T>(CollectionOutcome.Unavailable, default, null)
                    : new CollectionAnswer<T>(CollectionOutcome.Succeeded, value, null);
            }
            catch (JsonException)
            {
                // A 2xx with a body the copies cannot read is a contract drift, reported as no usable answer.
                return new CollectionAnswer<T>(CollectionOutcome.Unavailable, default, null);
            }
        }
    }

    private async Task<CollectionAnswer> SendEmptyAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        using (request)
        {
            using var response = await TrySendAsync(request, cancellationToken);
            if (response is null)
            {
                return new CollectionAnswer(CollectionOutcome.Unavailable, null);
            }

            return response.IsSuccessStatusCode
                ? new CollectionAnswer(CollectionOutcome.Succeeded, null)
                : new CollectionAnswer(FromStatus(response.StatusCode), await ProblemCodeAsync(response, cancellationToken));
        }
    }

    private async Task<CollectionAnswer<string>> SendTextAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        using (request)
        {
            using var response = await TrySendAsync(request, cancellationToken);
            if (response is null)
            {
                return new CollectionAnswer<string>(CollectionOutcome.Unavailable, null, null);
            }

            if (!response.IsSuccessStatusCode)
            {
                return new CollectionAnswer<string>(FromStatus(response.StatusCode), null, await ProblemCodeAsync(response, cancellationToken));
            }

            return new CollectionAnswer<string>(CollectionOutcome.Succeeded, await response.Content.ReadAsStringAsync(cancellationToken), null);
        }
    }

    /// <summary>
    /// Sends one request through the named client. A transport failure or timeout is no answer (null). The caller's own
    /// cancellation still propagates, so a circuit that closed stops its calls.
    /// </summary>
    private async Task<HttpResponseMessage?> TrySendAsync(HttpRequestMessage request, CancellationToken cancellationToken, HttpCompletionOption completion = HttpCompletionOption.ResponseContentRead)
    {
        try
        {
            return await clients.CreateClient(BackendServices.CollectionsName).SendAsync(request, completion, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is HttpRequestException or OperationCanceledException or TimeoutException)
        {
            return null;
        }
    }

    /// <summary>The problem's <c>code</c> extension (CONTRACTS §0), or null when the body is not a problem with one.</summary>
    private static Task<string?> ProblemCodeAsync(HttpResponseMessage response, CancellationToken cancellationToken) =>
        ProblemResponses.ProblemCodeAsync(response, cancellationToken);

    private static CollectionOutcome FromStatus(HttpStatusCode status) => ProblemResponses.FromStatus(status);
}
