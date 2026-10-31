using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace PostmanClone.Web.Api;

/// <summary>
/// What an EnvironmentsService call came back with. <see cref="Code"/> is the problem's <c>code</c> when there was one, and
/// <see cref="Errors"/> the field messages of a 400 <c>validation_failed</c> (CONTRACTS §0), keyed by member name.
/// </summary>
public sealed record EnvironmentAnswer<T>(CollectionOutcome Outcome, T? Value, string? Code, IReadOnlyDictionary<string, string[]>? Errors = null);

/// <summary>A call that returns nothing on success (a delete).</summary>
public sealed record EnvironmentAnswer(CollectionOutcome Outcome, string? Code, IReadOnlyDictionary<string, string[]>? Errors = null);

/// <summary>
/// The Web's client for EnvironmentsService (CONTRACTS §3, §5). Each call takes the bearer it needs and passes it through
/// <see cref="AuthenticatedRequest"/>; the client keeps no token, and the workspace comes from that token. Failures become
/// outcomes, never exceptions. The client does not call <c>resolve</c>: that is ExecutionService's call, made server to server.
/// </summary>
public sealed class EnvironmentsClient(IHttpClientFactory clients)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public Task<EnvironmentAnswer<PageResult<EnvironmentSummary>>> ListEnvironmentsAsync(string accessToken, int page, int pageSize, CancellationToken cancellationToken) =>
        SendAsync<PageResult<EnvironmentSummary>>(
            AuthenticatedRequest.Create(HttpMethod.Get, $"/api/v1/environments?page={page}&pageSize={pageSize}", accessToken),
            cancellationToken);

    public Task<EnvironmentAnswer<EnvironmentDto>> GetEnvironmentAsync(string accessToken, Guid environmentId, CancellationToken cancellationToken) =>
        SendAsync<EnvironmentDto>(AuthenticatedRequest.Create(HttpMethod.Get, $"/api/v1/environments/{environmentId}", accessToken), cancellationToken);

    /// <summary>Creates an environment. The service answers <c>409</c> when the workspace already has its 50 (CONTRACTS §3).</summary>
    public Task<EnvironmentAnswer<EnvironmentDto>> CreateEnvironmentAsync(string accessToken, CreateEnvironmentBody body, CancellationToken cancellationToken) =>
        SendAsync<EnvironmentDto>(JsonRequest(HttpMethod.Post, "/api/v1/environments", accessToken, body), cancellationToken);

    /// <summary>Replaces the name and the whole variable set. The answer is the environment as stored, with no secret value in it.</summary>
    public Task<EnvironmentAnswer<EnvironmentDto>> UpdateEnvironmentAsync(string accessToken, Guid environmentId, UpdateEnvironmentBody body, CancellationToken cancellationToken) =>
        SendAsync<EnvironmentDto>(JsonRequest(HttpMethod.Put, $"/api/v1/environments/{environmentId}", accessToken, body), cancellationToken);

    public Task<EnvironmentAnswer> DeleteEnvironmentAsync(string accessToken, Guid environmentId, CancellationToken cancellationToken) =>
        SendEmptyAsync(AuthenticatedRequest.Create(HttpMethod.Delete, $"/api/v1/environments/{environmentId}", accessToken), cancellationToken);

    private static HttpRequestMessage JsonRequest<T>(HttpMethod method, string path, string accessToken, T body)
    {
        var request = AuthenticatedRequest.Create(method, path, accessToken);
        request.Content = JsonContent.Create(body, options: Json);
        return request;
    }

    private async Task<EnvironmentAnswer<T>> SendAsync<T>(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        using (request)
        {
            using var response = await TrySendAsync(request, cancellationToken);
            if (response is null)
            {
                return new EnvironmentAnswer<T>(CollectionOutcome.Unavailable, default, null);
            }

            if (!response.IsSuccessStatusCode)
            {
                var problem = await ReadProblemAsync(response, cancellationToken);
                return new EnvironmentAnswer<T>(ProblemResponses.FromStatus(response.StatusCode), default, problem.Code, problem.Errors);
            }

            try
            {
                var value = await response.Content.ReadFromJsonAsync<T>(Json, cancellationToken);
                return value is null
                    ? new EnvironmentAnswer<T>(CollectionOutcome.Unavailable, default, null)
                    : new EnvironmentAnswer<T>(CollectionOutcome.Succeeded, value, null);
            }
            catch (JsonException)
            {
                // A 2xx with a body the copies cannot read is a contract drift, reported as no usable answer.
                return new EnvironmentAnswer<T>(CollectionOutcome.Unavailable, default, null);
            }
        }
    }

    private async Task<EnvironmentAnswer> SendEmptyAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        using (request)
        {
            using var response = await TrySendAsync(request, cancellationToken);
            if (response is null)
            {
                return new EnvironmentAnswer(CollectionOutcome.Unavailable, null);
            }

            if (response.IsSuccessStatusCode)
            {
                return new EnvironmentAnswer(CollectionOutcome.Succeeded, null);
            }

            var problem = await ReadProblemAsync(response, cancellationToken);
            return new EnvironmentAnswer(ProblemResponses.FromStatus(response.StatusCode), problem.Code, problem.Errors);
        }
    }

    /// <summary>
    /// Sends one request through the environments client. A transport failure or timeout is no answer (null). The caller's own
    /// cancellation still propagates, so a circuit that closed stops its calls.
    /// </summary>
    private async Task<HttpResponseMessage?> TrySendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        try
        {
            return await clients.CreateClient(BackendServices.EnvironmentsName).SendAsync(request, HttpCompletionOption.ResponseContentRead, cancellationToken);
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

    /// <summary>
    /// The problem's <c>code</c> and its <c>errors</c> map (CONTRACTS §0). Only the messages are read; no title, detail or
    /// value is logged or shown raw, and a body that is not a problem yields neither.
    /// </summary>
    private static async Task<(string? Code, IReadOnlyDictionary<string, string[]>? Errors)> ReadProblemAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return (null, null);
            }

            var code = root.TryGetProperty("code", out var codeValue) && codeValue.ValueKind == JsonValueKind.String ? codeValue.GetString() : null;
            Dictionary<string, string[]>? errors = null;
            if (root.TryGetProperty("errors", out var errorsValue) && errorsValue.ValueKind == JsonValueKind.Object)
            {
                errors = [];
                foreach (var field in errorsValue.EnumerateObject())
                {
                    if (field.Value.ValueKind == JsonValueKind.Array)
                    {
                        errors[field.Name] = field.Value.EnumerateArray()
                            .Where(message => message.ValueKind == JsonValueKind.String)
                            .Select(message => message.GetString() ?? string.Empty)
                            .ToArray();
                    }
                }
            }

            return (code, errors);
        }
        catch (JsonException)
        {
            return (null, null);
        }
    }
}
