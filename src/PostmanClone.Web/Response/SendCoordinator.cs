using PostmanClone.Web.Agent;
using PostmanClone.Web.Api;

namespace PostmanClone.Web.Response;

/// <summary>Where a send is: nothing sent yet, in flight, a response to show, cancelled, or failed before any response.</summary>
public enum SendPhase
{
    Idle,
    Sending,
    Shown,
    Cancelled,
    Failed,
}

/// <summary>Which sender answered a send (WP31): the server, the user's local agent, or none yet.</summary>
public enum SendSender
{
    None,
    Server,
    Agent,
}

/// <summary>
/// The state the response panel draws. <see cref="RequestId"/> says which request the send was for; <see cref="Sender"/> says which
/// sender the send went through, so the panel can name it.
/// </summary>
public sealed record SendView(SendPhase Phase, Guid? RequestId, ResponseView? Response, string? Failure, SendSender Sender = SendSender.None)
{
    public static readonly SendView Idle = new(SendPhase.Idle, null, null, null);
}

/// <summary>Sends a request for the caller. The workspace comes from the session's token (ADR-0023); this seam is a fake in tests.</summary>
public interface IExecutionGateway
{
    Task<CollectionAnswer<ExecutionResultDto>> ExecuteAsync(ExecuteBody body, CancellationToken cancellationToken);
}

/// <summary>
/// Runs the circuit's sends (WP20, ADR-0023; WP31 adds the agent). When the send target is the agent, the send goes through
/// <see cref="IAgentRunner"/>; otherwise it goes through the server gateway, unchanged. One send is in flight at a time per circuit: a new send cancels the one before it,
/// and a result that arrives for a send that was superseded is dropped, so an old answer never overwrites a newer one. Cancelling
/// cancels the HTTP call, and leaves the page idle with no exception to observe. The page subscribes to <see cref="Changed"/>
/// and re-renders through <c>InvokeAsync</c>.
/// </summary>
public sealed class SendCoordinator(IExecutionGateway gateway, EnvironmentSelection environment, AgentConnection? agent = null, IAgentRunner? runner = null) : IDisposable
{
    private CancellationTokenSource? _inFlight;
    private long _sequence;
    private bool _disposed;

    public SendView View { get; private set; } = SendView.Idle;

    public bool IsSending => View.Phase == SendPhase.Sending;

    /// <summary>Raised whenever <see cref="View"/> changes, on the thread that changed it.</summary>
    public event Action? Changed;

    /// <summary>Sends a request. It returns when the send has finished, been cancelled or been superseded.</summary>
    public async Task SendAsync(Guid requestId, RequestDefinition request, Guid? savedRequestId, CancellationToken circuitCancellation)
    {
        if (_disposed)
        {
            return;
        }

        var sequence = ++_sequence;
        _inFlight?.Cancel();
        var cancellation = CancellationTokenSource.CreateLinkedTokenSource(circuitCancellation);
        _inFlight = cancellation;
        Show(new SendView(SendPhase.Sending, requestId, null, null));

        var body = new ExecuteBody(request, environment.EnvironmentId, savedRequestId, null, null);
        var viaAgent = agent is not null && runner is not null && agent.EffectiveTarget == SendTarget.Agent;
        var sender = viaAgent ? SendSender.Agent : SendSender.Server;
        try
        {
            if (viaAgent)
            {
                var run = await runner!.SendAsync(body, cancellation.Token);
                if (sequence != _sequence)
                {
                    return;
                }

                Show(run.Result is { } agentResult
                    ? new SendView(SendPhase.Shown, requestId, ResponseView.From(agentResult), null, sender)
                    : new SendView(SendPhase.Failed, requestId, null, run.Failure, sender));
                return;
            }

            var answer = await gateway.ExecuteAsync(body, cancellation.Token);
            if (sequence != _sequence)
            {
                return;
            }

            Show(answer.Value is { } result
                ? new SendView(SendPhase.Shown, requestId, ResponseView.From(result), null, sender)
                : new SendView(SendPhase.Failed, requestId, null, ResponseMessages.ForFailure(answer.Outcome, answer.Code), sender));
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            if (sequence == _sequence)
            {
                Show(new SendView(SendPhase.Cancelled, requestId, null, ResponseMessages.Cancelled, sender));
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // An unexpected failure is shown as an unavailable sender, with no exception text on the page.
            if (sequence == _sequence)
            {
                Show(new SendView(SendPhase.Failed, requestId, null, ResponseMessages.ForFailure(CollectionOutcome.Unavailable, null), sender));
            }
        }
        finally
        {
            if (ReferenceEquals(_inFlight, cancellation))
            {
                _inFlight = null;
            }

            cancellation.Dispose();
        }
    }

    /// <summary>Cancels the send in flight, if any. The view returns to Cancelled once the call has stopped.</summary>
    public void Cancel() => _inFlight?.Cancel();

    /// <summary>The view for one request: the send's view when it was for that request, otherwise nothing.</summary>
    public SendView ViewFor(Guid requestId) => View.RequestId == requestId ? View : SendView.Idle;

    public void Dispose()
    {
        _disposed = true;
        _inFlight?.Cancel();
    }

    private void Show(SendView view)
    {
        View = view;
        Changed?.Invoke();
    }
}
