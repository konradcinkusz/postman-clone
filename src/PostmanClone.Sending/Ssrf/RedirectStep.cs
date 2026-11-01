namespace PostmanClone.Sending.Ssrf;

public enum RedirectOutcome
{
    /// <summary>The response is final: it is not a redirect, or it has no Location.</summary>
    NotRedirect,

    /// <summary>Send the next request to <see cref="RedirectStep.Next"/>.</summary>
    Follow,

    /// <summary>Stop: the redirect is refused, and <see cref="RedirectStep.ErrorCode"/> says why.</summary>
    Refuse,
}

/// <summary>What to do with a redirect response. <see cref="CrossOrigin"/> tells the sender which headers to drop.</summary>
public sealed record RedirectStep(
    RedirectOutcome Outcome,
    SsrfTarget? Next,
    string? Method,
    bool DropBody,
    bool CrossOrigin,
    string? ErrorCode)
{
    public static RedirectStep NotRedirect { get; } = new(RedirectOutcome.NotRedirect, null, null, false, false, null);

    public static RedirectStep Refuse(string errorCode) => new(RedirectOutcome.Refuse, null, null, false, false, errorCode);
}
