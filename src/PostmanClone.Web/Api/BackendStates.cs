namespace PostmanClone.Web.Api;

/// <summary>The backend states the Web shows, and how each one is styled.</summary>
public static class BackendStates
{
    public const string Unreachable = "unreachable";
    public const string Unknown = "unknown";

    /// <summary>
    /// The badge class for a state. A state the Web does not recognise is neutral, never green: a status that cannot
    /// be read must not look healthy.
    /// </summary>
    public static string BadgeFor(string state) => state switch
    {
        "healthy" => "badge-success",
        "degraded" => "badge-warning",
        "unhealthy" or Unreachable => "badge-danger",
        _ => string.Empty,
    };
}
