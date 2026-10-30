namespace PostmanClone.ExecutionService.Clients;

/// <summary>Where EnvironmentsService is reached and how long one call may take, bound from <c>Environments:</c>.</summary>
public sealed class EnvironmentsClientOptions
{
    public const string SectionName = "Environments";

    /// <summary>The origin only (no path). Under the AppHost, service discovery resolves the logical name.</summary>
    public Uri BaseAddress { get; set; } = new("http://environments-service");

    /// <summary>One call, from connect to the last byte of the body. The kernel's resilience handler may retry inside it.</summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(10);
}
