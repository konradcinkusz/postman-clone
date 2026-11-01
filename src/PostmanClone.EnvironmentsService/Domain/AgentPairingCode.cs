namespace PostmanClone.EnvironmentsService.Domain;

/// <summary>
/// A one-time pairing code (ADR-0026 Decision 3, CONTRACTS A2.2). Only the hash of the code is stored. A code is single-use
/// (<see cref="ConsumedAt"/>) and lives five minutes. A new code for the same user replaces an unused one.
/// </summary>
public sealed class AgentPairingCode
{
    public Guid Id { get; set; }

    public Guid WorkspaceId { get; set; }

    /// <summary>The user who asked for the code. The agent it pairs is theirs.</summary>
    public string UserId { get; set; } = string.Empty;

    /// <summary>SHA-256 of the normalised code, lower-case hex.</summary>
    public string CodeHash { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset ExpiresAt { get; set; }

    public DateTimeOffset? ConsumedAt { get; set; }
}
