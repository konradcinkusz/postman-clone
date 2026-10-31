namespace PostmanClone.CollectionsService.Tests.Support;

/// <summary>
/// Literal values that look like secrets, used only to check that an import never keeps them (ADR-0006). They are fixtures,
/// not credentials, and each line that holds one carries a <c>gitleaks:allow</c> comment for the secret scanner.
/// </summary>
public static class SecretLiterals
{
    public const string Bearer = "fixture-bearer-do-not-use-0001"; // gitleaks:allow

    public const string BasicPassword = "fixture-basic-password-0002"; // gitleaks:allow

    public const string ApiKey = "fixture-api-key-0003"; // gitleaks:allow

    public const string AuthorizationHeader = "Bearer fixture-header-0004"; // gitleaks:allow

    public const string QueryToken = "fixture-query-token-0005"; // gitleaks:allow

    public const string FormPassword = "fixture-form-password-0006"; // gitleaks:allow

    /// <summary>A collection in which every place a secret can sit holds a literal: auth helpers, a sensitive header, a sensitive query and a sensitive form field.</summary>
    public static byte[] Collection() => PostmanFixtures.Utf8(
        "{\"info\":{\"name\":\"Literals\"},\"item\":[" +
        "{\"name\":\"Bearer literal\",\"request\":{\"method\":\"GET\",\"url\":\"https://example.invalid/a\"," +
        "\"auth\":{\"type\":\"bearer\",\"bearer\":[{\"key\":\"token\",\"value\":\"" + Bearer + "\"}]}}}," +
        "{\"name\":\"Basic literal\",\"request\":{\"method\":\"GET\",\"url\":\"https://example.invalid/b\"," +
        "\"auth\":{\"type\":\"basic\",\"basic\":[{\"key\":\"username\",\"value\":\"svc\"},{\"key\":\"password\",\"value\":\"" + BasicPassword + "\"}]}}}," +
        "{\"name\":\"Key literal\",\"request\":{\"method\":\"GET\",\"url\":\"https://example.invalid/c\"," +
        "\"auth\":{\"type\":\"apikey\",\"apikey\":[{\"key\":\"key\",\"value\":\"X-Api-Key\"},{\"key\":\"value\",\"value\":\"" + ApiKey + "\"},{\"key\":\"in\",\"value\":\"header\"}]}}}," +
        "{\"name\":\"Header literal\",\"request\":{\"method\":\"GET\",\"url\":\"https://example.invalid/d\"," +
        "\"header\":[{\"key\":\"Authorization\",\"value\":\"" + AuthorizationHeader + "\"}]}}," +
        "{\"name\":\"Query literal\",\"request\":{\"method\":\"GET\",\"url\":\"https://example.invalid/e?token=" + QueryToken + "\"}}," +
        "{\"name\":\"Form literal\",\"request\":{\"method\":\"POST\",\"url\":\"https://example.invalid/f\"," +
        "\"body\":{\"mode\":\"urlencoded\",\"urlencoded\":[{\"key\":\"password\",\"value\":\"" + FormPassword + "\"}]}}}" +
        "]}");

    /// <summary>Every literal above, for checks that none of them reached the stored plan or the warnings.</summary>
    public static readonly string[] All = [Bearer, BasicPassword, ApiKey, AuthorizationHeader, QueryToken, FormPassword];
}
