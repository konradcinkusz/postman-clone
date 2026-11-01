using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using PostmanClone.ExecutionService.Clients;
using PostmanClone.ExecutionService.History;
using PostmanClone.Sending.Assembly;
using PostmanClone.Sending.Contracts;
using PostmanClone.Sending.Execution;

namespace PostmanClone.ExecutionService.Sending;

/// <summary>
/// The server's sender (CONTRACTS §4, ADR-0014, ADR-0026 Decision 8): the shared <see cref="OutboundSender"/> with the SSRF
/// rules, plus what only the server does. It fetches the environment's variables with the caller's own token, and it
/// records the masked result in the caller's history (ADR-0015). The order is fixed: shape, variables, send, history.
/// </summary>
public sealed class RequestExecutor(
    OutboundSender sender,
    EnvironmentsClient environments,
    IHistoryRecorder history,
    ILogger<RequestExecutor> logger)
{
    public async Task<ExecutionResult> ExecuteAsync(ExecuteRequest request, ExecutionCaller caller, CancellationToken cancellationToken)
    {
        var prepared = sender.Prepare(request);
        var variables = await ResolveVariablesAsync(request.EnvironmentId, caller, cancellationToken);
        var sent = await sender.SendAsync(prepared, variables, cancellationToken);

        var historyId = await RecordHistoryAsync(
            new HistoryDraft(caller.WorkspaceId, caller.UserId, request.SavedRequestId, request.EnvironmentId, sent.Definition, sent.Masker, sent.Result),
            cancellationToken);
        return sent.Result with { HistoryId = historyId };
    }

    /// <summary>
    /// The history is a record of the send, not part of it (ADR-0015): a failure to store is logged by its type only, and the
    /// caller gets the result with no history id.
    /// </summary>
    private async Task<Guid?> RecordHistoryAsync(HistoryDraft draft, CancellationToken cancellationToken)
    {
        try
        {
            return await history.RecordAsync(draft, cancellationToken);
        }
        catch (Exception failure)
        {
            logger.LogWarning("The history entry could not be recorded: {FailureType}.", failure.GetType().Name);
            return null;
        }
    }

    /// <summary>The enabled variables of the environment, or none. An environment the caller may not see is absent, so 404.</summary>
    private async Task<IReadOnlyDictionary<string, ResolvedValue>> ResolveVariablesAsync(Guid? environmentId, ExecutionCaller caller, CancellationToken cancellationToken)
    {
        var variables = new Dictionary<string, ResolvedValue>(StringComparer.Ordinal);
        if (environmentId is null)
        {
            return variables;
        }

        var environment = await environments.ResolveAsync(environmentId.Value, caller.AccessToken, cancellationToken)
            ?? throw new ExecuteFailure(StatusCodes.Status404NotFound, "not_found", "The environment was not found.");

        foreach (var variable in environment.Variables)
        {
            variables[variable.Key] = new ResolvedValue(variable.Value, variable.IsSecret);
        }

        return variables;
    }
}
