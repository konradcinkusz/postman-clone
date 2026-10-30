namespace PostmanClone.ExecutionService.Clients;

/// <summary>
/// The answer of EnvironmentsService <c>POST /api/v1/environments/{id}/resolve</c> (CONTRACTS §3). A copy of the
/// shape, not a reference to EnvironmentsService: the services share no code. Secret values are plaintext here,
/// so this type is never logged or returned to a caller.
/// </summary>
public sealed record ResolvedEnvironment(Guid Id, IReadOnlyList<ResolvedVariable> Variables);

public sealed record ResolvedVariable(string Key, string Value, bool IsSecret);
