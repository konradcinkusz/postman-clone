namespace PostmanClone.Web.Api;

// The Web's copies of the agent routes' types (CONTRACTS A2.2, A2.5). The shapes follow EnvironmentsService's AgentDtos and
// ExecutionService's HistoryRecordWrite; a renamed field fails the round-trip test that reads the service's own types.

/// <summary>A pairing code: shown once, five minutes (A2.2).</summary>
public sealed record PairingCodeDto(string Code, DateTimeOffset ExpiresAt);

/// <summary>One agent of the caller in the active workspace (A2.2). <see cref="RevokedAt"/> is set once it is revoked.</summary>
public sealed record AgentInstallationDto(
    Guid Id,
    string Name,
    string Platform,
    string Version,
    DateTimeOffset PairedAt,
    DateTimeOffset? LastUsedAt,
    DateTimeOffset ExpiresAt,
    DateTimeOffset? RevokedAt);

/// <summary>
/// The body of <c>POST /api/v1/history/records</c> (A2.5), and of the Web's <c>POST /agent/runs</c>. The request is the template
/// with its references; the result was masked by the agent. <c>Source</c> is always <c>"agent"</c>.
/// </summary>
public sealed record HistoryRecordWriteDto(
    string Source,
    RequestDefinition Request,
    Guid? EnvironmentId,
    Guid? SavedRequestId,
    ExecutionResultDto Result);
