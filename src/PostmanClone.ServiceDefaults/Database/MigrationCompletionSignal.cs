using System.Runtime.CompilerServices;

namespace PostmanClone.ServiceDefaults.Database;

/// <summary>
/// Completes once the schema is applied, or once the attempt has failed (SERVICE-API-PATTERNS §7).
/// Background work that touches the database awaits <see cref="WaitAsync"/> first, so it never runs
/// against a missing table.
/// </summary>
public sealed class MigrationCompletionSignal
{
    private readonly TaskCompletionSource _finished = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private volatile bool _succeeded;

    /// <summary>True only after the schema was applied successfully.</summary>
    public bool Succeeded => _succeeded;

    /// <summary>True once the attempt has ended, successfully or not.</summary>
    public bool Finished => _finished.Task.IsCompleted;

    /// <summary>Waits until the attempt ends. Throws when it failed, so the caller does not start.</summary>
    public async Task WaitAsync(CancellationToken cancellationToken = default)
    {
        await _finished.Task.WaitAsync(cancellationToken);
        if (!_succeeded)
        {
            throw new InvalidOperationException("Database migration failed; background work cannot start.");
        }
    }

    internal void MarkSucceeded()
    {
        _succeeded = true;
        _finished.TrySetResult();
    }

    internal void MarkFailed() => _finished.TrySetResult();
}
