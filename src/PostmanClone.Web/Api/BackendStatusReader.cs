using System.Net.Http.Json;

namespace PostmanClone.Web.Api;

/// <summary>One backend's state as the status strip shows it.</summary>
public sealed record BackendStatus(string Name, string State);

public interface IBackendStatusReader
{
    Task<IReadOnlyList<BackendStatus>> ReadAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Reads <c>GET /health</c> from each backend (CONTRACTS §0). A backend that cannot be reached is reported as
/// <see cref="BackendStates.Unreachable"/>: one missing service does not hide the others.
/// </summary>
public sealed class BackendStatusReader(IHttpClientFactory clients) : IBackendStatusReader
{
    public async Task<IReadOnlyList<BackendStatus>> ReadAsync(CancellationToken cancellationToken)
    {
        var statuses = new List<BackendStatus>(BackendServices.All.Count);
        foreach (var backend in BackendServices.All)
        {
            var state = await ReadStateAsync(clients.CreateClient(backend.Name), cancellationToken);
            statuses.Add(new BackendStatus(backend.Name, state));
        }

        return statuses;
    }

    private static async Task<string> ReadStateAsync(HttpClient client, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await client.GetAsync("/health", cancellationToken);
            // The body is read for its status only; a 503 still carries it.
            var payload = await response.Content.ReadFromJsonAsync<Dictionary<string, string>>(cancellationToken);
            return payload?.GetValueOrDefault("status") ?? BackendStates.Unknown;
        }
        catch (HttpRequestException)
        {
            return BackendStates.Unreachable;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // A timeout, not a caller's cancellation.
            return BackendStates.Unreachable;
        }
        catch (System.Text.Json.JsonException)
        {
            return BackendStates.Unknown;
        }
    }
}
