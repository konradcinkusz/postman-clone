using System.Net.Http.Json;
using System.Text.Json;

namespace PostmanClone.Web.Api;

/// <summary>
/// The Web's client for ExecutionService's sender (CONTRACTS §4, §5). One call, <c>POST /api/v1/execute</c>; the caller's
/// cancellation stops the HTTP call (the service sees the connection close). A failure is an outcome, never an exception,
/// so a down backend cannot blank the page (BLAZOR-WEB-APP §5).
/// </summary>
public interface IExecutionClient
{
    Task<CollectionAnswer<ExecutionResultDto>> ExecuteAsync(string accessToken, ExecuteBody body, CancellationToken cancellationToken);
}

public sealed class ExecutionClient(IHttpClientFactory clients) : IExecutionClient
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<CollectionAnswer<ExecutionResultDto>> ExecuteAsync(string accessToken, ExecuteBody body, CancellationToken cancellationToken)
    {
        using var request = AuthenticatedRequest.Create(HttpMethod.Post, "/api/v1/execute", accessToken);
        request.Content = JsonContent.Create(body, options: Json);

        var client = clients.CreateClient(BackendServices.ExecutionName);
        HttpResponseMessage response;
        try
        {
            response = await client.SendAsync(request, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is HttpRequestException or OperationCanceledException or TimeoutException)
        {
            // No answer: a refused connection, or the resilience handler's own timeout. Neither has a status or a code.
            return new CollectionAnswer<ExecutionResultDto>(CollectionOutcome.Unavailable, null, null);
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                return new CollectionAnswer<ExecutionResultDto>(
                    ProblemResponses.FromStatus(response.StatusCode),
                    null,
                    await ProblemResponses.ProblemCodeAsync(response, cancellationToken));
            }

            try
            {
                var result = await response.Content.ReadFromJsonAsync<ExecutionResultDto>(Json, cancellationToken);
                return result is null
                    ? new CollectionAnswer<ExecutionResultDto>(CollectionOutcome.Unavailable, null, null)
                    : new CollectionAnswer<ExecutionResultDto>(CollectionOutcome.Succeeded, result, null);
            }
            catch (JsonException)
            {
                // A body that is not the contract is an unusable answer, not a result to show.
                return new CollectionAnswer<ExecutionResultDto>(CollectionOutcome.Unavailable, null, null);
            }
        }
    }
}
