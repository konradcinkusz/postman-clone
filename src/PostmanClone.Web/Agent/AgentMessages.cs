namespace PostmanClone.Web.Agent;

/// <summary>
/// The sentences the agent pages show (WP31). Each names what the user can do; none carries a code, a token, a URL of the agent or a
/// value from a response.
/// </summary>
public static class AgentMessages
{
    public const string NotRunning = "The agent is not running on this computer. Start it, then try again.";
    public const string NotPaired = "This computer is not paired with the agent. Pair it on the Agent page.";
    public const string PairAgain = "The agent no longer accepts this page. Pair again on the Agent page.";
    public const string Refused = "The agent does not accept requests from this page. Its setup must name this page's address (the Agent page shows the command).";
    public const string Rejected = "The agent could not send this request. Check the request and try again.";
    public const string Unavailable = "The agent answered with a problem. Try again in a moment.";
    public const string Cancelled = "The send was cancelled.";

    public const string CodeInvalid = "The pairing code is not valid, has been used, or has expired. Create a new code.";
    public const string PairingUnavailable = "The agent could not finish the pairing. Check that the agent is running, then try again.";
    public const string AgentHasNoServer = "The agent has no server yet. Run its setup command first (the Agent page shows it).";
    public const string Paired = "This page is paired with the agent. Sends can now go through it.";
    public const string Revoked = "The agent was revoked. Pair again to use it.";
    public const string Created = "A pairing code was created. Enter it on this page, or run the pair command in a terminal.";

    /// <summary>The page's message for a short code in the query string (<see cref="AgentCodes"/>).</summary>
    public static string ForCode(string? code) => code switch
    {
        AgentCodes.NotFound => "That agent is no longer in your workspace.",
        AgentCodes.Forbidden => "You are not allowed to do that in this workspace.",
        AgentCodes.Unavailable => "The server could not be reached. Try again in a moment.",
        AgentCodes.Failed => "The request did not succeed. Try again.",
        _ => "The request did not succeed. Try again.",
    };
}
