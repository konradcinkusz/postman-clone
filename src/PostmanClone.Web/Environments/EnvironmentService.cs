using PostmanClone.Web.Api;
using PostmanClone.Web.Auth;
using PostmanClone.Web.Response;

namespace PostmanClone.Web.Environments;

/// <summary>
/// The circuit's environment calls (WP21). Each call takes the session's access token from <see cref="CircuitSession"/> and passes
/// it on, so the circuit holds no token of its own. The workspace is the token's, never a parameter. The service also keeps the
/// list the top bar shows and the environment the sends use (<see cref="EnvironmentSelection"/>), and keeps both in step with
/// the saves and deletes the page makes.
/// </summary>
public sealed class EnvironmentService(WorkspaceState workspace, CircuitSession session, EnvironmentsClient client, EnvironmentSelection selection)
{
    /// <summary>One page covers the workspace's 50 environments (CONTRACTS §3), so the list needs no paging.</summary>
    public const int ListPageSize = 100;

    /// <summary>The list as last read, or null until it has been read.</summary>
    public IReadOnlyList<EnvironmentSummary>? Summaries { get; private set; }

    /// <summary>Raised when the list changes, on the thread that changed it.</summary>
    public event Action? Changed;

    public bool HasWorkspace => workspace.Resolution.IsActive;

    public async Task<EnvironmentAnswer<IReadOnlyList<EnvironmentSummary>>> ListAsync(CancellationToken cancellationToken)
    {
        var answer = await WithTokenAsync(
            (token, ct) => client.ListEnvironmentsAsync(token, 1, ListPageSize, ct),
            cancellationToken);
        if (answer.Value is not { } page)
        {
            return new EnvironmentAnswer<IReadOnlyList<EnvironmentSummary>>(answer.Outcome, null, answer.Code);
        }

        Summaries = page.Items;
        Changed?.Invoke();
        return new EnvironmentAnswer<IReadOnlyList<EnvironmentSummary>>(CollectionOutcome.Succeeded, page.Items, null);
    }

    public Task<EnvironmentAnswer<EnvironmentDto>> GetAsync(Guid environmentId, CancellationToken cancellationToken) =>
        WithTokenAsync((token, ct) => client.GetEnvironmentAsync(token, environmentId, ct), cancellationToken);

    /// <summary>Creates an environment with a name only. The list is read again so the new row carries the service's values.</summary>
    public async Task<EnvironmentAnswer<EnvironmentDto>> CreateAsync(string name, CancellationToken cancellationToken)
    {
        var answer = await WithTokenAsync(
            (token, ct) => client.CreateEnvironmentAsync(token, new CreateEnvironmentBody(name.Trim()), ct),
            cancellationToken);
        if (answer.Outcome == CollectionOutcome.Succeeded)
        {
            await ListAsync(cancellationToken);
        }

        return answer;
    }

    /// <summary>
    /// Saves the editor's name and variables. On success the editor takes the service's copy, and the list and the chosen environment
    /// take the new name and variable names. On a 400 the service's field messages go to the editor.
    /// </summary>
    public async Task<EnvironmentAnswer<EnvironmentDto>> SaveAsync(EnvironmentEditor editor, CancellationToken cancellationToken)
    {
        editor.ClearServerErrors();
        var body = editor.ToUpdateBody();
        var answer = await WithTokenAsync(
            (token, ct) => client.UpdateEnvironmentAsync(token, editor.Id, body, ct),
            cancellationToken);

        if (answer.Value is { } saved)
        {
            editor.MarkSaved(saved);
            ReplaceSummary(saved);
            if (selection.EnvironmentId == saved.Id)
            {
                selection.Select(saved.Id, saved.Name, EnabledKeys(saved));
            }

            Changed?.Invoke();
            return answer;
        }

        if (answer.Errors is { } errors)
        {
            editor.ApplyServerErrors(errors);
        }

        return answer;
    }

    /// <summary>Deletes an environment. If it was the chosen one, the choice is cleared, so no send carries a deleted id.</summary>
    public async Task<EnvironmentAnswer> DeleteAsync(Guid environmentId, CancellationToken cancellationToken)
    {
        var answer = await WithTokenAsync(
            (token, ct) => client.DeleteEnvironmentAsync(token, environmentId, ct),
            cancellationToken);
        if (answer.Outcome == CollectionOutcome.Succeeded)
        {
            Summaries = Summaries?.Where(summary => summary.Id != environmentId).ToList();
            if (selection.EnvironmentId == environmentId)
            {
                selection.Clear();
            }

            Changed?.Invoke();
        }

        return answer;
    }

    /// <summary>
    /// Chooses the environment the sends use, or none. The variable names come from the environment as stored, so the builder can
    /// mark the ones that do not resolve. An environment that is gone clears the choice.
    /// </summary>
    public async Task<EnvironmentAnswer<EnvironmentDto>> ChooseAsync(Guid? environmentId, CancellationToken cancellationToken)
    {
        if (environmentId is null)
        {
            selection.Select(null);
            return new EnvironmentAnswer<EnvironmentDto>(CollectionOutcome.Succeeded, null, null);
        }

        var answer = await GetAsync(environmentId.Value, cancellationToken);
        if (answer.Value is { } environment)
        {
            selection.Select(environment.Id, environment.Name, EnabledKeys(environment));
        }
        else if (answer.Outcome == CollectionOutcome.NotFound)
        {
            selection.Clear();
        }

        return answer;
    }

    /// <summary>The enabled variable names of an environment: the names a send resolves (CONTRACTS §4 uses enabled variables only).</summary>
    public static IReadOnlySet<string> EnabledKeys(EnvironmentDto environment) =>
        environment.Variables.Where(variable => variable.Enabled).Select(variable => variable.Key).ToHashSet(StringComparer.Ordinal);

    private void ReplaceSummary(EnvironmentDto saved)
    {
        if (Summaries is null)
        {
            return;
        }

        var summary = new EnvironmentSummary(saved.Id, saved.Name, saved.Variables.Count, saved.UpdatedAt);
        Summaries = Summaries.Select(existing => existing.Id == saved.Id ? summary : existing).ToList();
    }

    /// <summary>Runs a call with the session's token. No workspace or no session is an outcome, so the page can say which.</summary>
    private async Task<EnvironmentAnswer<T>> WithTokenAsync<T>(Func<string, CancellationToken, Task<EnvironmentAnswer<T>>> call, CancellationToken cancellationToken)
    {
        if (!HasWorkspace)
        {
            return new EnvironmentAnswer<T>(CollectionOutcome.NoWorkspace, default, null);
        }

        var token = await session.AccessTokenAsync(cancellationToken);
        return token is null
            ? new EnvironmentAnswer<T>(CollectionOutcome.SessionEnded, default, null)
            : await call(token, cancellationToken);
    }

    private async Task<EnvironmentAnswer> WithTokenAsync(Func<string, CancellationToken, Task<EnvironmentAnswer>> call, CancellationToken cancellationToken)
    {
        if (!HasWorkspace)
        {
            return new EnvironmentAnswer(CollectionOutcome.NoWorkspace, null);
        }

        var token = await session.AccessTokenAsync(cancellationToken);
        return token is null
            ? new EnvironmentAnswer(CollectionOutcome.SessionEnded, null)
            : await call(token, cancellationToken);
    }
}
