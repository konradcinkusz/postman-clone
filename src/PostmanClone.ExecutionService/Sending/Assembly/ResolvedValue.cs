namespace PostmanClone.ExecutionService.Sending.Assembly;

/// <summary>
/// A variable's value after resolution, with the flag that decides whether <see cref="SecretMasker"/> hides it.
/// The value is plaintext for secrets: it is never logged, never returned and never stored (ADR-0006).
/// </summary>
public sealed record ResolvedValue(string Value, bool IsSecret);
