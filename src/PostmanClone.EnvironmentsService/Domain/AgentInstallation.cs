namespace PostmanClone.EnvironmentsService.Domain;

/// <summary>
/// A paired agent (ADR-0026 Decision 3, CONTRACTS A2.2). The agent token is shown once and only its SHA-256 hash is stored.
/// The token has an expiry of 90 days from pairing and can be revoked per agent. Revocation is the only way it stops
/// before its expiry: a change of the user's membership does not revoke it (open item).
/// </summary>
public sealed class AgentInstallation
{
    public Guid Id { get; set; }

    public Guid WorkspaceId { get; set; }

    public string UserId { get; set; } = string.Empty;

    /// <summary>The name the agent gave itself at pairing, shown in the Web's list.</summary>
    public string Name { get; set; } = string.Empty;

    public string Platform { get; set; } = string.Empty;

    public string Version { get; set; } = string.Empty;

    /// <summary>SHA-256 of the agent token, lower-case hex.</summary>
    public string TokenHash { get; set; } = string.Empty;

    public DateTimeOffset PairedAt { get; set; }

    public DateTimeOffset ExpiresAt { get; set; }

    public DateTimeOffset? LastUsedAt { get; set; }

    public DateTimeOffset? RevokedAt { get; set; }
}
