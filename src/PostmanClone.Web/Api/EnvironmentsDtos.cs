namespace PostmanClone.Web.Api;

// Copies of EnvironmentsService's contract types (CONTRACTS §3, src/PostmanClone.EnvironmentsService/Api). The Web cannot
// reference the service, so the shapes are written by hand here. EnvironmentsRoundTripTests serializes the service's real
// types and reads them back through these copies, so a renamed or retyped field is a failing test.
//
// A secret's value is never in an EnvironmentDto: VariableDto.Value is null for a secret, and HasValue says whether one is
// stored. The write body carries a secret's value only when the user typed a new one (ADR-0024).

/// <summary>One row of the environment list (CONTRACTS §3).</summary>
public sealed record EnvironmentSummary(Guid Id, string Name, int VariableCount, DateTimeOffset UpdatedAt);

/// <summary>An environment with its variables. Secret values are never in it (CONTRACTS §3).</summary>
public sealed record EnvironmentDto(
    Guid Id,
    string Name,
    string CreatedBy,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    List<VariableDto> Variables);

/// <summary>
/// One stored variable as a client may read it. <see cref="Value"/> is null for a secret; <see cref="HasValue"/> says whether a
/// secret holds a value without revealing it.
/// </summary>
public sealed record VariableDto(string Key, string? Value, bool IsSecret, bool HasValue, bool Enabled);

/// <summary>The body of <c>POST /environments</c>.</summary>
public sealed record CreateEnvironmentBody(string Name);

/// <summary>The body of <c>PUT /environments/{id}</c>: a name and the complete variable set (CONTRACTS §3).</summary>
public sealed record UpdateEnvironmentBody(string Name, List<VariableWriteBody> Variables);

/// <summary>
/// One variable of a <c>PUT</c>. For a secret with no typed value, <see cref="KeepExistingSecret"/> asks the service to keep the
/// ciphertext it already holds; <see cref="Value"/> is then null, so the page never sends a secret it does not have.
/// </summary>
public sealed record VariableWriteBody(string Key, string? Value, bool IsSecret, bool Enabled, bool KeepExistingSecret);
