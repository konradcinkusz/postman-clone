namespace PostmanClone.Sending.Execution;

/// <summary>
/// A request the sender refuses before anything is sent: the endpoint answers with <see cref="StatusCode"/> and
/// <see cref="Code"/>. The message names a field and never a value (ADR-0006, ADR-0012).
/// </summary>
public sealed class ExecuteFailure(int statusCode, string code, string message) : Exception(message)
{
    public int StatusCode { get; } = statusCode;

    /// <summary>A stable snake_case code (CONTRACTS §0).</summary>
    public string Code { get; } = code;
}
