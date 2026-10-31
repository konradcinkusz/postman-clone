namespace PostmanClone.ServiceDefaults.Resilience;

/// <summary>
/// Total timeouts for named HttpClients, read by the kernel's one resilience handler (ADR-0023). A client that is not listed keeps
/// the library's default. The handler is still the kernel's: only its limits differ per client, so no client adds a second handler.
/// </summary>
public sealed class HttpClientTimeouts
{
    /// <summary>The total time one call may take, keyed by the HttpClient's name.</summary>
    public Dictionary<string, TimeSpan> Totals { get; } = new(StringComparer.Ordinal);
}
