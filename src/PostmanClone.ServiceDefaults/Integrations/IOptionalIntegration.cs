namespace PostmanClone.ServiceDefaults.Integrations;

/// <summary>
/// An external dependency that is configured optionally (P8). Register one per integration; its status is
/// reported on <c>/health</c> and in the startup banner, and never fails the service.
/// </summary>
public interface IOptionalIntegration
{
    /// <summary>The key shown on <c>/health</c>, for example <c>secrets</c>.</summary>
    string Name { get; }

    /// <summary><see cref="OptionalIntegrationStatus.Available"/> or <see cref="OptionalIntegrationStatus.Unavailable"/>.</summary>
    string Status { get; }
}

public static class OptionalIntegrationStatus
{
    public const string Available = "available";
    public const string Unavailable = "unavailable";
}
