using System.Text.Json;
using Microsoft.AspNetCore.Components.Forms;
using PostmanClone.Web.Api;

namespace PostmanClone.Web.Agent;

/// <summary>The body the page sends the agent: the <c>AgentSendRequest</c> of CONTRACTS A2.6 (an <c>ExecuteRequest</c> without a saved request).</summary>
public sealed record AgentSendBody(RequestDefinition Request, Guid? EnvironmentId, int? TimeoutSeconds, bool? FollowRedirects);

/// <summary>What a send through the agent produced: a result to show, or the sentence to show instead.</summary>
public sealed record AgentRunAnswer(ExecutionResultDto? Result, string? Failure);

/// <summary>Sends a request through the agent for the circuit (ADR-0028). The seam is a fake in the Web's tests.</summary>
public interface IAgentRunner
{
    Task<AgentRunAnswer> SendAsync(ExecuteBody body, CancellationToken cancellationToken);
}

/// <summary>
/// Sends through the local agent and records the run (WP31). The agent answers with the masked result; the page then posts that result to
/// <c>/agent/runs</c>, which masks names and stores it through ExecutionService. A recording that fails does not hide the result: the
/// answer is shown without a history id. A refusal from the agent changes the connection state (a revoked token drops the page's pairing).
/// </summary>
public sealed class AgentRunner(AgentConnection connection, AgentBridge bridge, AntiforgeryStateProvider antiforgery) : IAgentRunner
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<AgentRunAnswer> SendAsync(ExecuteBody body, CancellationToken cancellationToken)
    {
        var origin = connection.Origin;
        var token = connection.PairingToken;
        if (origin is null || token is null)
        {
            return new AgentRunAnswer(null, MessageFor(connection.Status));
        }

        var sendId = Guid.NewGuid().ToString("N");
        var bodyJson = JsonSerializer.Serialize(new AgentSendBody(body.Request, body.EnvironmentId, body.TimeoutSeconds, body.FollowRedirects), Json);
        var reply = await bridge.SendAsync(origin, token, bodyJson, sendId, cancellationToken);

        if (reply.Status != 200)
        {
            return Refusal(reply.Status);
        }

        if (Parse<ExecutionResultDto>(reply.Text) is not { } result)
        {
            return new AgentRunAnswer(null, AgentMessages.Unavailable);
        }

        var record = new HistoryRecordWriteDto("agent", body.Request, body.EnvironmentId, body.SavedRequestId, result);
        var stored = await bridge.PostRunAsync(JsonSerializer.Serialize(record, Json), AntiforgeryValue(), cancellationToken);
        var historyId = stored.Status == 201 && Parse<HistoryRecordedDto>(stored.Text) is { } recorded ? recorded.Id : (Guid?)null;
        return new AgentRunAnswer(result with { HistoryId = historyId }, null);
    }

    private AgentRunAnswer Refusal(int status)
    {
        switch (status)
        {
            case 401 or 409:
                // The agent does not know the page's token any more: the page must pair again.
                connection.ForgetPairing();
                return new AgentRunAnswer(null, AgentMessages.PairAgain);
            case 403 or 421:
                connection.MarkRefused();
                return new AgentRunAnswer(null, AgentMessages.Refused);
            case 400 or 413 or 415:
                return new AgentRunAnswer(null, AgentMessages.Rejected);
            case 0:
                connection.MarkUnreachable();
                return new AgentRunAnswer(null, AgentMessages.NotRunning);
            default:
                return new AgentRunAnswer(null, AgentMessages.Unavailable);
        }
    }

    private static string MessageFor(AgentStatus status) => status switch
    {
        AgentStatus.NotRunning => AgentMessages.NotRunning,
        AgentStatus.NotPaired => AgentMessages.NotPaired,
        _ => AgentMessages.PairAgain,
    };

    private string AntiforgeryValue() => antiforgery.GetAntiforgeryToken()?.Value ?? string.Empty;

    private static T? Parse<T>(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return default;
        }

        try
        {
            return JsonSerializer.Deserialize<T>(text, Json);
        }
        catch (JsonException)
        {
            return default;
        }
    }
}
