namespace PostmanClone.EnvironmentsService.Secrets;

/// <summary>
/// Used when <c>Secrets:EncryptionKey</c> is missing or invalid. Secret writes and reads answer
/// <c>503 secrets_unavailable</c> (CONTRACTS §3); everything else keeps working (P8).
/// </summary>
public sealed class UnavailableSecretProtector : ISecretProtector
{
    public bool IsAvailable => false;

    public string Protect(string plaintext, string associatedData) => throw new SecretsUnavailableException();

    public string Unprotect(string envelope, string associatedData) => throw new SecretsUnavailableException();
}

/// <summary>Raised when a secret value must be stored or read but no valid encryption key is configured.</summary>
public sealed class SecretsUnavailableException : InvalidOperationException
{
    public SecretsUnavailableException()
        : base("Secret variables are unavailable: the encryption key is not configured.")
    {
    }
}
