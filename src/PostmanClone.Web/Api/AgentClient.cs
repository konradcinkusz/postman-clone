using System.Net.Http.Json;
using System.Text.Json;

namespace PostmanClone.Web.Api;

/// <summary>
/// The Web's client for the agent's user routes: pairing codes, the caller's installations and their revocation (EnvironmentsService,
/// A2.2), and the run history of a send made by the agent (ExecutionService, A2.5). Each call takes the caller's bearer. A failure is
/// an outcome, never an exception (BLAZOR-WEB-APP §5). The agent's own routes are never called from the server: the browser calls
/// the agent, and the agent calls EnvironmentsService with its own token (ADR-0026 Decision 4).
/// </summary>
public interface IAgentClient
{
    Task<CollectionAnswer<PairingCodeDto>> CreatePairingCodeAsync(string accessToken, CancellationToken cancellationToken);

    Task<CollectionAnswer<PageResult<AgentInstallationDto>>> ListInstallationsAsync(string accessToken, CancellationToken cancellationToken);

    Task<CollectionAnswer> RevokeInstallationAsync(string accessToken, Guid installationId, CancellationToken cancellationToken);

    Task<CollectionAnswer<HistoryRecordedDto>> RecordRunAsync(string accessToken, HistoryRecordWriteDto record, CancellationToken cancellationToken);
}

/// <summary>The part of a recorded run the page needs: the id of the history entry that now holds it.</summary>
public sealed record HistoryRecordedDto(Guid Id);

public sealed class AgentClient(IHttpClientFactory clients) : IAgentClient
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public Task<CollectionAnswer<PairingCodeDto>> CreatePairingCodeAsync(string accessToken, CancellationToken cancellationToken) =>
        ReadAsync<PairingCodeDto, object>(BackendServices.EnvironmentsName, HttpMethod.Post, "/api/v1/agent/pairing-codes", accessToken, null, cancellationToken);

    public Task<CollectionAnswer<PageResult<AgentInstallationDto>>> ListInstallationsAsync(string accessToken, CancellationToken cancellationToken) =>
        ReadAsync<PageResult<AgentInstallationDto>, object>(BackendServices.EnvironmentsName, HttpMethod.Get, "/api/v1/agent/installations?page=1&pageSize=100", accessToken, null, cancellationToken);

    public async Task<CollectionAnswer> RevokeInstallationAsync(string accessToken, Guid installationId, CancellationToken cancellationToken)
    {
        var answer = await SendEmptyAsync(BackendServices.EnvironmentsName, HttpMethod.Delete, $"/api/v1/agent/installations/{installationId}", accessToken, cancellationToken);
        return new CollectionAnswer(answer.Outcome, answer.Code);
    }

    public Task<CollectionAnswer<HistoryRecordedDto>> RecordRunAsync(string accessToken, HistoryRecordWriteDto record, CancellationToken cancellationToken) =>
        ReadAsync<HistoryRecordedDto, HistoryRecordWriteDto>(BackendServices.ExecutionName, HttpMethod.Post, "/api/v1/history/records", accessToken, record, cancellationToken);

    private async Task<CollectionAnswer<T>> ReadAsync<T, TBody>(string backend, HttpMethod method, string path, string accessToken, TBody? body, CancellationToken cancellationToken)
        where TBody : class
    {
        using var request = AuthenticatedRequest.Create(method, path, accessToken);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body, options: Json);
        }

        using var response = await Send(backend, request, cancellationToken);
        if (response is null)
        {
            return new CollectionAnswer<T>(CollectionOutcome.Unavailable, default, null);
        }

        if (!response.IsSuccessStatusCode)
        {
            return new CollectionAnswer<T>(ProblemResponses.FromStatus(response.StatusCode), default, await ProblemResponses.ProblemCodeAsync(response, cancellationToken));
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
            // A body that is not the contract is an unusable answer, not a value to show.
            return new CollectionAnswer<T>(CollectionOutcome.Unavailable, default, null);
        }
    }

    private async Task<CollectionAnswer> SendEmptyAsync(string backend, HttpMethod method, string path, string accessToken, CancellationToken cancellationToken)
    {
        using var request = AuthenticatedRequest.Create(method, path, accessToken);
        using var response = await Send(backend, request, cancellationToken);
        if (response is null)
        {
            return new CollectionAnswer(CollectionOutcome.Unavailable, null);
        }

        return response.IsSuccessStatusCode
            ? new CollectionAnswer(CollectionOutcome.Succeeded, null)
            : new CollectionAnswer(ProblemResponses.FromStatus(response.StatusCode), await ProblemResponses.ProblemCodeAsync(response, cancellationToken));
    }

    /// <summary>The answer, or null when there was none (a refused connection or the handler's own timeout).</summary>
    private async Task<HttpResponseMessage?> Send(string backend, HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var client = clients.CreateClient(backend);
        try
        {
            return await client.SendAsync(request, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return null;
        }
    }
}
