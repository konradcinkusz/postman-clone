namespace PostmanClone.EnvironmentsService.Secrets;

/// <summary>
/// The associated data that binds a secret to its place: <c>{workspaceId}|{environmentId}|{key}</c> (ADR-0006,
/// CONTRACTS §3). Writing and reading must build it the same way, so it lives in one place.
/// </summary>
public static class SecretPlace
{
    public static string For(Guid workspaceId, Guid environmentId, string key) => $"{workspaceId}|{environmentId}|{key}";
}
