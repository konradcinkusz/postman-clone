using Microsoft.Extensions.Logging;

namespace PostmanClone.EnvironmentsService.Environments;

/// <summary>
/// The audit and warning lines of the environment API. They carry ids and counts, never a variable value, a key's
/// value or a ciphertext (ADR-0006). A log line that needs more than that is refused in review.
/// </summary>
public sealed partial class EnvironmentAudit
{
    [LoggerMessage(EventId = 1, Level = LogLevel.Information,
        Message = "Resolved environment {EnvironmentId} for user {UserId}: {Count} variables.")]
    public static partial void Resolved(ILogger logger, Guid environmentId, string userId, int count);

    [LoggerMessage(EventId = 2, Level = LogLevel.Warning,
        Message = "A stored secret of environment {EnvironmentId} could not be decrypted; the encryption key may not match.")]
    public static partial void SecretUndecryptable(ILogger logger, Guid environmentId);
}
