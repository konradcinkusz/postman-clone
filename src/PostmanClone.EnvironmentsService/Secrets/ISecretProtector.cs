namespace PostmanClone.EnvironmentsService.Secrets;

/// <summary>
/// Encrypts secret variable values for storage and decrypts them for <c>resolve</c> (ADR-0006, CONTRACTS §3).
/// The associated data binds a ciphertext to its place (<c>{workspaceId}|{environmentId}|{key}</c>), so a value
/// copied to another variable or environment does not decrypt.
/// </summary>
public interface ISecretProtector
{
    /// <summary>True when a valid key is configured. When false, <see cref="Protect"/> and <see cref="Unprotect"/> throw.</summary>
    bool IsAvailable { get; }

    /// <summary>Encrypts <paramref name="plaintext"/> and returns the <c>v1:</c> envelope.</summary>
    string Protect(string plaintext, string associatedData);

    /// <summary>Decrypts an envelope made by <see cref="Protect"/>. Throws <see cref="System.Security.Cryptography.CryptographicException"/> on any mismatch.</summary>
    string Unprotect(string envelope, string associatedData);
}
