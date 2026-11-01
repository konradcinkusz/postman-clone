using Microsoft.Extensions.Logging;
using PostmanClone.Agent.Config;
using PostmanClone.Agent.Remote;
using PostmanClone.Sending.Assembly;
using PostmanClone.Sending.Contracts;
using PostmanClone.Sending.Execution;
using PostmanClone.Sending.Limits;
using PostmanClone.Sending.Ssrf;

namespace PostmanClone.Agent.Send;

/// <summary>
/// Runs one send for the browser (ADR-0026 Decision 6). It is the shared <see cref="OutboundSender"/> with the agent's rules: the
/// contract's caps, no SSRF guard unless <c>AllowPrivate</c> is off, and the refusal of the agent's own listener. The environment's
/// variables are fetched with the agent token for this send only, and are not kept.
/// </summary>
public sealed class AgentSender(IAgentServer server, ILoggerFactory loggers)
{
    public async Task<SendReply> SendAsync(AgentConfig config, AgentSendRequest request, int port, CancellationToken cancellationToken)
    {
        var variables = new Dictionary<string, ResolvedValue>(StringComparer.Ordinal);
        if (request.EnvironmentId is { } environmentId)
        {
            var resolved = await server.ResolveAsync(config.ServerBaseUrl!, config.AgentToken!, environmentId, cancellationToken);
            if (resolved.Status == 401)
            {
                return SendReply.Refused(409, "agent_not_paired", "The agent is not paired. Pair it again from the Web.");
            }

            if (!resolved.Ok)
            {
                // The message does not say whether the environment exists (CONTRACTS A2.6).
                return SendReply.Sent(Unavailable());
            }

            foreach (var variable in resolved.Body!.Variables)
            {
                variables[variable.Key] = new ResolvedValue(variable.Value, variable.IsSecret);
            }
        }

        var rules = new OutboundRules(
            new SsrfOptions
            {
                // Local and private targets are the point of the agent (ADR-0026). The flag turns the guard's ranges back on.
                AllowLoopback = config.AllowPrivate,
                AllowPrivateRanges = config.AllowPrivate,
                BlockedPorts = [],
            },
            ownPort: port);
        var sender = new OutboundSender(
            SendingLimits.Contract,
            rules,
            new SystemHostResolver(),
            new SocketAddressDialer(),
            loggers.CreateLogger<OutboundSender>());

        try
        {
            var prepared = sender.Prepare(new ExecuteRequest(
                request.Request,
                request.EnvironmentId,
                null,
                request.TimeoutSeconds,
                request.FollowRedirects));
            var sent = await sender.SendAsync(prepared, variables, cancellationToken);
            return SendReply.Sent(sent.Result);
        }
        catch (ExecuteFailure failure)
        {
            return SendReply.Refused(failure.StatusCode, failure.Code, failure.Message);
        }
    }

    private static ExecutionResult Unavailable() =>
        new(
            ExecutionOutcome.InvalidRequest,
            "environment_unavailable",
            "The environment could not be used for this send.",
            null,
            new ExecutionTiming(0, 0, 0, 0, 0),
            string.Empty,
            [],
            [],
            null);
}
