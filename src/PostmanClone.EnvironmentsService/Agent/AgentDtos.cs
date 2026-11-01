using System.ComponentModel.DataAnnotations;

namespace PostmanClone.EnvironmentsService.Agent;

/// <summary>The body of <c>POST /api/v1/agent/pair</c> (CONTRACTS A2.3).</summary>
public sealed class AgentPairRequest
{
    [Required]
    [StringLength(20, MinimumLength = 10)]
    public string Code { get; set; } = string.Empty;

    [Required]
    [StringLength(100, MinimumLength = 1)]
    public string AgentName { get; set; } = string.Empty;

    [Required]
    [StringLength(40, MinimumLength = 1)]
    public string Platform { get; set; } = string.Empty;

    [Required]
    [StringLength(40, MinimumLength = 1)]
    public string Version { get; set; } = string.Empty;
}

/// <summary>A pairing code and when it stops working. The code is shown once (CONTRACTS A2.2).</summary>
public sealed record PairingCodeDto(string Code, DateTimeOffset ExpiresAt);

/// <summary>The agent's credentials. <see cref="AgentToken"/> is shown once and never again (CONTRACTS A2.3).</summary>
public sealed record AgentPairing(
    Guid AgentId,
    string AgentToken,
    DateTimeOffset ExpiresAt,
    Guid WorkspaceId,
    IReadOnlyList<string> AllowedWebOrigins);

/// <summary>One paired agent, as the user sees it (CONTRACTS A2.2).</summary>
public sealed record AgentInstallationDto(
    Guid Id,
    string Name,
    string Platform,
    string Version,
    DateTimeOffset PairedAt,
    DateTimeOffset? LastUsedAt,
    DateTimeOffset ExpiresAt,
    DateTimeOffset? RevokedAt);

/// <summary>What an agent sees of itself (CONTRACTS A2.4).</summary>
public sealed record AgentIdentity(Guid AgentId, Guid WorkspaceId, DateTimeOffset ExpiresAt);
