namespace PostmanClone.EnvironmentsService.Domain;

/// <summary>
/// One variable of an environment. A plain variable stores <see cref="Value"/>. A secret stores only
/// <see cref="Ciphertext"/> (the <c>v1:</c> envelope, ADR-0006) and leaves <see cref="Value"/> null, so a database
/// read never yields a secret value.
/// </summary>
public sealed class Variable
{
    public Guid Id { get; set; }

    /// <summary>Copied from the environment, so every variable query can be scoped to the workspace.</summary>
    public Guid WorkspaceId { get; set; }

    public Guid EnvironmentId { get; set; }

    public string Key { get; set; } = string.Empty;

    /// <summary>The plain value. Null for a secret.</summary>
    public string? Value { get; set; }

    /// <summary>The encrypted secret value. Null for a plain variable.</summary>
    public string? Ciphertext { get; set; }

    public bool IsSecret { get; set; }

    public bool Enabled { get; set; } = true;
}
