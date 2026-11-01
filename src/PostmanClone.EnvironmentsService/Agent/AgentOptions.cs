namespace PostmanClone.EnvironmentsService.Agent;

/// <summary>
/// The agent's settings, bound from <c>Agent:</c>. <see cref="AllowedWebOrigins"/> is the only list of pages the agent
/// accepts a browser request from (ADR-0026 Decision 5, CONTRACTS A2.6). It is set in the server's configuration, not by a
/// user, so a page can only reach the agent if the operator named its origin.
/// </summary>
public sealed class AgentOptions
{
    public const string SectionName = "Agent";

    public string[] AllowedWebOrigins { get; set; } = [];
}

/// <summary>Refuses an origin list that could widen what the agent accepts: a wildcard, a path, or a non-origin text.</summary>
public static class AgentOptionsValidator
{
    /// <summary>The problems with the list, one per entry that is not an exact origin. Empty when the list is valid.</summary>
    public static IReadOnlyList<string> Problems(IEnumerable<string> origins)
    {
        var problems = new List<string>();
        foreach (var origin in origins)
        {
            if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri)
                || uri.Scheme is not ("https" or "http")
                || uri.AbsolutePath != "/"
                || !string.IsNullOrEmpty(uri.Query)
                || !string.IsNullOrEmpty(uri.Fragment)
                || !string.IsNullOrEmpty(uri.UserInfo)
                || origin.Contains('*', StringComparison.Ordinal)
                || origin.EndsWith('/'))
            {
                problems.Add("Agent:AllowedWebOrigins holds an entry that is not an exact origin such as https://app.example.");
            }
        }

        return problems;
    }
}
