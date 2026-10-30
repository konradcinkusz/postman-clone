using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace PostmanClone.ExecutionService.Clients;

/// <summary>
/// Calls EnvironmentsService for the variables of one environment (CONTRACTS §3, §4). It forwards the caller's own
/// access token, so EnvironmentsService applies the same workspace check to the same user; the service never reads
/// variables from the database of another service. The token is set per request and is never logged.
/// </summary>
public sealed class EnvironmentsClient(HttpClient http, ILogger<EnvironmentsClient> logger)
{
    private static readonly JsonSerializerOptions JsonOptions = JsonSerializerOptions.Web;

    /// <summary>
    /// Resolves an environment's enabled variables, secrets in plaintext. Returns <c>null</c> when EnvironmentsService
    /// answers 404 (absent, or in another workspace: the same answer, CONTRACTS §0). Any other failure, including a
    /// redirect (redirects are not followed), a timeout or an unreadable body, throws <see cref="UpstreamUnavailableException"/>.
    /// </summary>
    public async Task<ResolvedEnvironment?> ResolveAsync(Guid environmentId, string accessToken, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/environments/{environmentId}/resolve")
        {
            Headers = { Authorization = new AuthenticationHeaderValue("Bearer", accessToken) },
        };

        var started = Stopwatch.GetTimestamp();
        try
        {
            using var response = await http.SendAsync(request, cancellationToken);
            logger.LogInformation(
                "EnvironmentsService {Method} {Host} answered {StatusCode} in {ElapsedMs} ms.",
                request.Method, http.BaseAddress?.Host, (int)response.StatusCode, Stopwatch.GetElapsedTime(started).TotalMilliseconds);

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return null;
            }

            if (!response.IsSuccessStatusCode)
            {
                throw new UpstreamUnavailableException($"EnvironmentsService answered {(int)response.StatusCode}.");
            }

            return await response.Content.ReadFromJsonAsync<ResolvedEnvironment>(JsonOptions, cancellationToken)
                ?? throw new UpstreamUnavailableException("EnvironmentsService answered an empty body.");
        }
        catch (HttpRequestException exception)
        {
            throw new UpstreamUnavailableException("EnvironmentsService could not be reached.", exception);
        }
        catch (TaskCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            // The HttpClient timeout fired. A cancellation from the caller is not an upstream failure and is not caught here.
            throw new UpstreamUnavailableException("EnvironmentsService did not answer in time.", exception);
        }
        catch (JsonException exception)
        {
            throw new UpstreamUnavailableException("EnvironmentsService answered a body that does not match the contract.", exception);
        }
    }
}
