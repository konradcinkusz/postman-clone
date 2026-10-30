namespace PostmanClone.ExecutionService.Sending.Assembly;

/// <summary>
/// A non-fatal finding about the assembled request. The message never carries a variable value.
/// </summary>
public sealed record AssemblyWarning(string Code, string Message);
