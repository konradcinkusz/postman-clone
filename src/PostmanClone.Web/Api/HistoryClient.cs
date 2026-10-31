using System.Net.Http.Json;
using System.Text.Json;

namespace PostmanClone.Web.Api;

/// <summary>
/// The Web's client for the history routes of ExecutionService (CONTRACTS §4, ADR-0015). Each call takes the bearer it needs; the
/// client keeps no token, and the service answers only for the caller's own entries (a row of another user or workspace is 404).
/// A failure is an outcome, never an exception, so a down sender cannot blank the page (BLAZOR-WEB-APP §5).
/// </summary>
public interface IHistoryClient
{
    Task<CollectionAnswer<PageResult<HistorySummaryDto>>> ListAsync(string accessToken, int page, int pageSize, string? query, CancellationToken cancellationToken);

    Task<CollectionAnswer<HistoryEntryDto>> GetAsync(string accessToken, Guid entryId, CancellationToken cancellationToken);

    Task<CollectionAnswer> DeleteAsync(string accessToken, Guid entryId, CancellationToken cancellationToken);

    Task<CollectionAnswer> ClearAsync(string accessToken, CancellationToken cancellationToken);
}

public sealed class HistoryClient(IHttpClientFactory clients) : IHistoryClient
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>The largest page the list asks for. The service clamps its own page size (CONTRACTS §0).</summary>
    public const int MaximumPageSize = 100;

    public Task<CollectionAnswer<PageResult<HistorySummaryDto>>> ListAsync(string accessToken, int page, int pageSize, string? query, CancellationToken cancellationToken)
    {
        var path = $"/api/v1/history?page={page}&pageSize={Math.Clamp(pageSize, 1, MaximumPageSize)}";
        if (!string.IsNullOrWhiteSpace(query))
        {
            path += "&q=" + Uri.EscapeDataString(query.Trim());
        }

        return ReadAsync<PageResult<HistorySummaryDto>>(HttpMethod.Get, path, accessToken, cancellationToken);
    }

    public Task<CollectionAnswer<HistoryEntryDto>> GetAsync(string accessToken, Guid entryId, CancellationToken cancellationToken) =>
        ReadAsync<HistoryEntryDto>(HttpMethod.Get, $"/api/v1/history/{entryId}", accessToken, cancellationToken);

    public async Task<CollectionAnswer> DeleteAsync(string accessToken, Guid entryId, CancellationToken cancellationToken)
    {
        var answer = await SendEmptyAsync(HttpMethod.Delete, $"/api/v1/history/{entryId}", accessToken, cancellationToken);
        return new CollectionAnswer(answer.Outcome, answer.Code);
    }

    public async Task<CollectionAnswer> ClearAsync(string accessToken, CancellationToken cancellationToken)
    {
        var answer = await SendEmptyAsync(HttpMethod.Delete, "/api/v1/history", accessToken, cancellationToken);
        return new CollectionAnswer(answer.Outcome, answer.Code);
    }

    private async Task<CollectionAnswer<T>> ReadAsync<T>(HttpMethod method, string path, string accessToken, CancellationToken cancellationToken)
    {
        using var request = AuthenticatedRequest.Create(method, path, accessToken);
        using var response = await Send(request, cancellationToken);
        if (response is null)
        {
            return new CollectionAnswer<T>(CollectionOutcome.Unavailable, default, null);
        }

        if (!response.IsSuccessStatusCode)
        {
            return new CollectionAnswer<T>(
                ProblemResponses.FromStatus(response.StatusCode),
                default,
                await ProblemResponses.ProblemCodeAsync(response, cancellationToken));
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
            // A body that is not the contract is an unusable answer, not a list to show.
            return new CollectionAnswer<T>(CollectionOutcome.Unavailable, default, null);
        }
    }

    private async Task<CollectionAnswer> SendEmptyAsync(HttpMethod method, string path, string accessToken, CancellationToken cancellationToken)
    {
        using var request = AuthenticatedRequest.Create(method, path, accessToken);
        using var response = await Send(request, cancellationToken);
        if (response is null)
        {
            return new CollectionAnswer(CollectionOutcome.Unavailable, null);
        }

        return response.IsSuccessStatusCode
            ? new CollectionAnswer(CollectionOutcome.Succeeded, null)
            : new CollectionAnswer(ProblemResponses.FromStatus(response.StatusCode), await ProblemResponses.ProblemCodeAsync(response, cancellationToken));
    }

    /// <summary>The answer, or null when there was none (a refused connection or the handler's own timeout).</summary>
    private async Task<HttpResponseMessage?> Send(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var client = clients.CreateClient(BackendServices.ExecutionName);
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
