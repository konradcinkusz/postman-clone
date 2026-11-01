namespace PostmanClone.Agent.Config;

/// <summary>
/// Loads and saves the agent's configuration for the listener and the commands. Writes are serialised, so a pairing and a
/// setup cannot interleave their updates. Each read is from disk: the file is small, and the commands share it.
/// </summary>
public sealed class AgentRuntime(AgentConfigStore store)
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    public AgentConfig Load() => store.Load();

    public async Task UpdateAsync(Action<AgentConfig> change, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var config = store.Load();
            change(config);
            store.Save(config);
        }
        finally
        {
            _gate.Release();
        }
    }
}
