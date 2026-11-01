namespace PostmanClone.ExecutionService.History;

/// <summary>
/// Who is sending, from the verified token. The workspace comes from the token's <c>organization</c> claim (CONTRACTS §0);
/// the access token is forwarded to EnvironmentsService and is never logged or printed, so this type has no record
/// equality or string form that would show it.
/// </summary>
public sealed class ExecutionCaller(Guid workspaceId, string userId, string accessToken)
{
    public Guid WorkspaceId { get; } = workspaceId;

    public string UserId { get; } = userId;

    public string AccessToken { get; } = accessToken;

    public override string ToString() => $"caller of workspace {WorkspaceId}";
}
