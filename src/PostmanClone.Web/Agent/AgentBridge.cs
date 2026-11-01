using Microsoft.JSInterop;

namespace PostmanClone.Web.Agent;

/// <summary>The probe's answer from the browser: <c>answered</c> (with the paired flag), <c>refused</c> or <c>unreachable</c>.</summary>
public sealed record JsProbeReply(string State, string? Base, bool Paired);

/// <summary>An HTTP answer from the agent as the browser saw it. <see cref="Status"/> 0 means no answer at all.</summary>
public sealed record JsHttpReply(int Status, string? Text);

/// <summary>
/// The only code that calls <c>wwwroot/agent-client.js</c> (ADR-0028). Every call is made by the circuit and answered by the script, which
/// does the fetch. What crosses this boundary: the loopback origin, the agent's pairing token, a JSON body built by the Web, and the
/// JSON answer. The session token never crosses it, and neither does a secret value: a send carries the environment's id, and the agent
/// resolves the values itself (ADR-0026 Decision 4).
/// </summary>
public sealed class AgentBridge(IJSRuntime js)
{
    /// <summary>The script's functions, as the page's global object names them.</summary>
    private const string Client = "agentClient";

    /// <summary>Probes the origins in order. A script that did not load reads as unreachable: the page then uses the server.</summary>
    public async Task<JsProbeReply> ProbeAsync(IReadOnlyList<string> origins, CancellationToken cancellationToken)
    {
        try
        {
            return await js.InvokeAsync<JsProbeReply>($"{Client}.probe", cancellationToken, origins)
                ?? new JsProbeReply("unreachable", null, false);
        }
        catch (JSException)
        {
            return new JsProbeReply("unreachable", null, false);
        }
    }

    public Task<JsHttpReply> PairAsync(string origin, string code, CancellationToken cancellationToken) =>
        js.InvokeAsync<JsHttpReply>($"{Client}.pair", cancellationToken, origin, code).AsTask();

    /// <summary>
    /// Sends the JSON body to the agent with the pairing token. The send id names the fetch, so a cancellation of this call (the user
    /// cancels, or the circuit closes) aborts the fetch in the browser as well.
    /// </summary>
    public async Task<JsHttpReply> SendAsync(string origin, string pairingToken, string bodyJson, string sendId, CancellationToken cancellationToken)
    {
        using var registration = cancellationToken.Register(() => _ = CancelQuietlyAsync(sendId));
        return await js.InvokeAsync<JsHttpReply>($"{Client}.send", cancellationToken, origin, pairingToken, bodyJson, sendId);
    }

    /// <summary>Posts the masked run to the Web's own <c>/agent/runs</c>, with the antiforgery token the page holds.</summary>
    public Task<JsHttpReply> PostRunAsync(string recordJson, string antiforgeryToken, CancellationToken cancellationToken) =>
        js.InvokeAsync<JsHttpReply>($"{Client}.postRun", cancellationToken, recordJson, antiforgeryToken).AsTask();

    private async Task CancelQuietlyAsync(string sendId)
    {
        try
        {
            await js.InvokeVoidAsync($"{Client}.cancel", sendId);
        }
        catch (Exception exception) when (exception is JSException or JSDisconnectedException or ObjectDisposedException or InvalidOperationException)
        {
            // The circuit is closing or the page has gone: the fetch ends with the page, so there is nothing left to cancel.
        }
    }
}
