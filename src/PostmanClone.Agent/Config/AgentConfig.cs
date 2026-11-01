using System.Text.Json.Serialization;

namespace PostmanClone.Agent.Config;

/// <summary>
/// What the agent keeps on disk (ADR-0026 Decision 11, CONTRACTS A2.6). It holds the agent token, which is a credential, and
/// the hash of the local pairing token, never the token itself. It holds no secret value and no request. The file is
/// readable by the user alone (<see cref="AgentConfigStore"/>).
/// </summary>
public sealed class AgentConfig
{
    /// <summary>The EnvironmentsService base URL the agent talks to (for example <c>https://estate.example</c>).</summary>
    public string? ServerBaseUrl { get; set; }

    /// <summary>
    /// The origins the browser may call from, exactly as written (CONTRACTS A2.6). Set at setup, and replaced by the server's list
    /// when a pairing succeeds.
    /// </summary>
    public List<string> AllowedWebOrigins { get; set; } = [];

    public Guid? AgentId { get; set; }

    public Guid? WorkspaceId { get; set; }

    /// <summary>The agent token, shown once by the server and stored here. Treat the file as a credential.</summary>
    public string? AgentToken { get; set; }

    public DateTimeOffset? AgentTokenExpiresAt { get; set; }

    /// <summary>SHA-256 hex of the local pairing token the browser holds. The token itself is never stored.</summary>
    public string? PairingTokenHash { get; set; }

    /// <summary>Whether this agent may send to private addresses (ADR-0026 Decision 6). Defaults to true.</summary>
    public bool AllowPrivate { get; set; } = true;

    [JsonIgnore]
    public bool IsPaired => AgentId is not null && !string.IsNullOrEmpty(AgentToken) && !string.IsNullOrEmpty(PairingTokenHash);
}
