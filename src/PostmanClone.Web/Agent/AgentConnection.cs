using System.Text.Json;

namespace PostmanClone.Web.Agent;

/// <summary>The outcome of a pairing from this page: whether it paired, and the one sentence to show.</summary>
public sealed record PairingResult(bool Paired, string Message);

/// <summary>
/// The circuit's view of the local agent (WP31, ADR-0028): what the last probe found, the origin that answered, the page's pairing token
/// and the user's send choice. The pairing token lives here, in circuit memory, and nowhere else: it is not rendered, not persisted, not
/// in a URL and not in browser storage. A reload or a closed page loses it, and the status then reads "Pair again".
/// </summary>
public sealed class AgentConnection(AgentBridge bridge, AgentWebOptions options, TimeProvider time) : IDisposable
{
    /// <summary>How often the page probes the agent while it is open, so the top bar follows a start or a stop.</summary>
    public static readonly TimeSpan WatchInterval = TimeSpan.FromSeconds(30);

    private readonly CancellationTokenSource _lifetime = new();
    private bool _watching;
    private bool _disposed;

    public AgentProbe Probe { get; private set; } = AgentProbe.Unchecked;

    /// <summary>The user's explicit choice of sender. Null is the automatic default (<see cref="AgentStatusRules.EffectiveTarget"/>).</summary>
    public SendTarget? Choice { get; private set; }

    /// <summary>The loopback origin that answered the last probe, or null. Only this origin receives the page's calls.</summary>
    public string? Origin { get; private set; }

    /// <summary>The agent's pairing token for this page, or null. Held in memory only; never shown.</summary>
    public string? PairingToken { get; private set; }

    public bool PagePaired => PairingToken is not null;

    public AgentStatus Status => AgentStatusRules.Derive(Probe, PagePaired);

    public SendTarget EffectiveTarget => AgentStatusRules.EffectiveTarget(Choice, Probe, PagePaired);

    /// <summary>Raised whenever the state changes, on the thread that changed it. A subscriber re-renders through <c>InvokeAsync</c>.</summary>
    public event Action? Changed;

    public void Choose(SendTarget? target)
    {
        Choice = target;
        Changed?.Invoke();
    }

    /// <summary>Asks the browser whether an agent answers, and whether it is paired. The answer updates the status.</summary>
    public async Task ProbeAsync(CancellationToken cancellationToken)
    {
        var reply = await bridge.ProbeAsync(options.ConnectOrigins, cancellationToken);
        switch (reply.State)
        {
            case "answered":
                Origin = reply.Base;
                Probe = reply.Paired ? AgentProbe.Paired : AgentProbe.NotPaired;
                if (!reply.Paired)
                {
                    // The agent forgot its pairing: a token this page holds is no longer any use.
                    PairingToken = null;
                }

                break;
            case "refused":
                Origin = reply.Base ?? Origin;
                Probe = AgentProbe.Refused;
                break;
            default:
                Probe = AgentProbe.Unreachable;
                break;
        }

        Changed?.Invoke();
    }

    /// <summary>Probes now and then every <see cref="WatchInterval"/> until the circuit closes. Starts once.</summary>
    public async Task WatchAsync()
    {
        if (_watching || _disposed)
        {
            return;
        }

        _watching = true;
        try
        {
            using var timer = new PeriodicTimer(WatchInterval, time);
            await ProbeAsync(_lifetime.Token);
            while (await timer.WaitForNextTickAsync(_lifetime.Token))
            {
                await ProbeAsync(_lifetime.Token);
            }
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
            // The circuit closed: the watch ends with it.
        }
    }

    /// <summary>
    /// Pairs this page: the code goes to the agent, which exchanges it with the server and returns the page's token. The code is not
    /// kept by this method; the caller clears it whatever the answer.
    /// </summary>
    public async Task<PairingResult> PairAsync(string code, CancellationToken cancellationToken)
    {
        if (Origin is null || Probe == AgentProbe.Unreachable)
        {
            await ProbeAsync(cancellationToken);
        }

        if (Origin is null || Probe == AgentProbe.Unreachable)
        {
            return new PairingResult(false, AgentMessages.NotRunning);
        }

        var reply = await bridge.PairAsync(Origin, code, cancellationToken);
        switch (reply.Status)
        {
            case 201:
                if (TryReadPairingToken(reply.Text) is not { } token)
                {
                    return new PairingResult(false, AgentMessages.PairingUnavailable);
                }

                PairingToken = token;
                Probe = AgentProbe.Paired;
                Changed?.Invoke();
                return new PairingResult(true, AgentMessages.Paired);
            case 400:
                return new PairingResult(false, AgentMessages.CodeInvalid);
            case 409:
                return new PairingResult(false, AgentMessages.AgentHasNoServer);
            case 403 or 421:
                Probe = AgentProbe.Refused;
                Changed?.Invoke();
                return new PairingResult(false, AgentMessages.Refused);
            case 0:
                Probe = AgentProbe.Unreachable;
                Changed?.Invoke();
                return new PairingResult(false, AgentMessages.NotRunning);
            default:
                return new PairingResult(false, AgentMessages.PairingUnavailable);
        }
    }

    /// <summary>The agent refused the page's token (revoked, or pairing replaced): the page drops it and reads "Pair again".</summary>
    public void ForgetPairing()
    {
        PairingToken = null;
        Changed?.Invoke();
    }

    /// <summary>The agent did not answer a send: the probe is stale, so the status says so until the next probe.</summary>
    public void MarkUnreachable()
    {
        Probe = AgentProbe.Unreachable;
        Changed?.Invoke();
    }

    /// <summary>The agent refused this page's origin or host.</summary>
    public void MarkRefused()
    {
        Probe = AgentProbe.Refused;
        Changed?.Invoke();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _lifetime.Cancel();
        _lifetime.Dispose();
    }

    private static string? TryReadPairingToken(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(text);
            return document.RootElement.TryGetProperty("pairingToken", out var token) && token.ValueKind == JsonValueKind.String
                ? token.GetString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
