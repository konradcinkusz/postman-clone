namespace PostmanClone.EnvironmentsService.Tests.Environments;

/// <summary>Plaintext for the secret tests. The value is invented; it is not a credential.</summary>
public static class SecretFixtures
{
    public const string Plaintext = "fixture-secret-value-7f3a"; // gitleaks:allow

    /// <summary>A secret variable in the wire shape of <c>VariableWrite</c>.</summary>
    public static object SecretVariable(string key, string? value, bool keepExistingSecret = false) =>
        new { key, value, isSecret = true, enabled = true, keepExistingSecret };
}
