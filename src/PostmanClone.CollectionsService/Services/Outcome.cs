namespace PostmanClone.CollectionsService.Services;

/// <summary>
/// A refused operation: the HTTP status, the stable <c>code</c> of CONTRACTS §0 and, for validation, the messages by field.
/// </summary>
public sealed record Failure(int Status, string Code, string Title, IReadOnlyDictionary<string, string[]>? Errors = null);

/// <summary>The result of an operation that returns a value: the value, or the failure that refused it.</summary>
public readonly record struct Outcome<T>(T? Value, Failure? Failure)
{
    public static Outcome<T> Ok(T value) => new(value, null);

    public static implicit operator Outcome<T>(Failure failure) => new(default, failure);
}

/// <summary>The result of an operation that returns nothing: success, or the failure that refused it.</summary>
public readonly record struct Outcome(Failure? Failure)
{
    public static readonly Outcome Success = new(null);

    public static implicit operator Outcome(Failure failure) => new(failure);
}
