using PostmanClone.Web.Api;

namespace PostmanClone.Web.Environments;

/// <summary>What the service holds for one variable, as the page read it. A secret's value is not part of it.</summary>
/// <param name="Key">The key as stored.</param>
/// <param name="IsSecret">Whether the variable is stored as a secret.</param>
/// <param name="Enabled">Whether it was enabled.</param>
/// <param name="PlainValue">The value of a plain variable; null for a secret, which the service never returns.</param>
/// <param name="HasValue">For a secret: whether a value is stored (VariableDto.HasValue).</param>
public sealed record StoredVariable(string Key, bool IsSecret, bool Enabled, string? PlainValue, bool HasValue);

/// <summary>
/// One row of the variable table (ADR-0024). A stored secret's value is not in the row: the row knows that a value is stored, and
/// holds only what the user typed since, in <see cref="TypedSecret"/>. A typed value is never rendered back; the page's input
/// has no value attribute, so the markup carries nothing the user typed.
/// </summary>
public sealed class VariableRow
{
    /// <summary>A stable identity for the page: the row's input is keyed by it, so a removed row takes its typed value with it.</summary>
    public Guid Id { get; } = Guid.NewGuid();

    public string Key { get; set; } = string.Empty;

    public bool IsSecret { get; set; }

    public bool Enabled { get; set; } = true;

    /// <summary>The plain value. Empty for a secret row: a stored secret's value is never loaded here.</summary>
    public string Value { get; set; } = string.Empty;

    /// <summary>The new value the user typed for a secret, or null when none was typed. Only this is ever sent as a secret value.</summary>
    public string? TypedSecret { get; set; }

    /// <summary>What the service held when the page last read or saved it. Null for a row added and not yet saved.</summary>
    public StoredVariable? Stored { get; private set; }

    /// <summary>
    /// A stored secret with no typed value keeps its stored ciphertext (keepExistingSecret). That is possible only while the key is
    /// the one the secret was stored under: the ciphertext is bound to the key (ADR-0006), so a renamed secret must be typed again.
    /// </summary>
    public bool CanKeepStoredSecret =>
        IsSecret
        && !HasTypedSecret
        && Stored is { IsSecret: true, HasValue: true } stored
        && string.Equals(stored.Key, Key, StringComparison.Ordinal);

    public bool HasTypedSecret => !string.IsNullOrEmpty(TypedSecret);

    /// <summary>Whether the row differs from what the service holds, or holds a typed secret not yet saved.</summary>
    public bool IsChanged =>
        Stored is null
        || !string.Equals(Stored.Key, Key, StringComparison.Ordinal)
        || Stored.IsSecret != IsSecret
        || Stored.Enabled != Enabled
        || (!IsSecret && !string.Equals(Stored.PlainValue ?? string.Empty, Value, StringComparison.Ordinal))
        || HasTypedSecret;

    public static VariableRow FromStored(VariableDto dto)
    {
        var stored = new StoredVariable(dto.Key, dto.IsSecret, dto.Enabled, dto.IsSecret ? null : dto.Value, dto.HasValue);
        return new VariableRow
        {
            Key = dto.Key,
            IsSecret = dto.IsSecret,
            Enabled = dto.Enabled,
            Value = dto.IsSecret ? string.Empty : dto.Value ?? string.Empty,
            Stored = stored,
        };
    }

    /// <summary>A new plain variable, enabled, with no key yet.</summary>
    public static VariableRow NewPlain() => new();

    /// <summary>The problem with this row alone, or null when the service would accept it (duplicates are the editor's to find).</summary>
    public string? OwnProblem()
    {
        var keyProblem = EnvironmentRules.KeyProblem(Key);
        if (keyProblem is not null)
        {
            return keyProblem;
        }

        if (IsSecret)
        {
            if (HasTypedSecret)
            {
                return EnvironmentRules.ValueProblem(TypedSecret);
            }

            if (CanKeepStoredSecret)
            {
                return null;
            }

            return Stored is { IsSecret: true, HasValue: true }
                ? EnvironmentMessages.SecretRenamed
                : EnvironmentMessages.SecretNeedsValue;
        }

        if (Stored is { IsSecret: true } && Value.Length == 0)
        {
            return EnvironmentMessages.PlainNeedsValue;
        }

        return EnvironmentRules.ValueProblem(Value);
    }

    /// <summary>The write for this row (CONTRACTS §3). Call only when <see cref="OwnProblem"/> is null.</summary>
    public VariableWriteBody ToWrite()
    {
        if (!IsSecret)
        {
            return new VariableWriteBody(Key, Value, IsSecret: false, Enabled, KeepExistingSecret: false);
        }

        return HasTypedSecret
            ? new VariableWriteBody(Key, TypedSecret, IsSecret: true, Enabled, KeepExistingSecret: false)
            : new VariableWriteBody(Key, null, IsSecret: true, Enabled, KeepExistingSecret: true);
    }
}
