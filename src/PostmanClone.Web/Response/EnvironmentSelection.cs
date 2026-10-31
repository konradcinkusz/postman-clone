namespace PostmanClone.Web.Response;

/// <summary>
/// The environment the circuit's sends use (WP21, ADR-0024). It is held in the circuit's memory, not in the browser: the choice is
/// kept while the page stays open, and a send carries its id so ExecutionService resolves the variables (CONTRACTS §4).
/// <see cref="KnownKeys"/> is the set of enabled variable names the builder marks as known; it is null when no environment is
/// chosen, so nothing is marked as unresolved. Names only: no value is kept here.
/// </summary>
public sealed class EnvironmentSelection
{
    public Guid? EnvironmentId { get; private set; }

    /// <summary>The chosen environment's name, for the selector. Null when none is chosen.</summary>
    public string? Name { get; private set; }

    /// <summary>The enabled variable names (secret and plain) of the chosen environment, or null when none is chosen.</summary>
    public IReadOnlySet<string>? KnownKeys { get; private set; }

    /// <summary>Raised when the choice or its variable names change, on the thread that changed them.</summary>
    public event Action? Changed;

    /// <summary>Chooses an environment, with its name and its enabled variable names. Passing only an id keeps no names, so none are known.</summary>
    public void Select(Guid? environmentId, string? name = null, IReadOnlySet<string>? knownKeys = null)
    {
        EnvironmentId = environmentId;
        Name = environmentId is null ? null : name;
        KnownKeys = environmentId is null ? null : knownKeys ?? new HashSet<string>(StringComparer.Ordinal);
        Changed?.Invoke();
    }

    /// <summary>Forgets the choice, as when the chosen environment is deleted.</summary>
    public void Clear() => Select(null);
}
