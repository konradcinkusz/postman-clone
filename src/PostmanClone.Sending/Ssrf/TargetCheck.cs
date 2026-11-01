namespace PostmanClone.Sending.Ssrf;

/// <summary>The result of <see cref="TargetValidator.Validate"/>: a target to send, or the code and category of the refusal.</summary>
public readonly record struct TargetCheck(SsrfTarget? Target, string? ErrorCode, string? Category)
{
    public bool Accepted => Target is not null;

    public static TargetCheck Accept(SsrfTarget target) => new(target, null, null);

    public static TargetCheck Refuse(string errorCode, string? category = null) => new(null, errorCode, category);
}
