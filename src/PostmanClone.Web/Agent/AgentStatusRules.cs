namespace PostmanClone.Web.Agent;

/// <summary>What the browser's probe of the agent found (CONTRACTS A2.6, <c>GET /v1/status</c>).</summary>
public enum AgentProbe
{
    /// <summary>No probe has finished yet.</summary>
    Unchecked,
    /// <summary>No agent answered on any allowed loopback origin.</summary>
    Unreachable,
    /// <summary>An agent answered, but refused this page's origin or host (403 or 421).</summary>
    Refused,
    /// <summary>An agent answered and is not paired.</summary>
    NotPaired,
    /// <summary>An agent answered and is paired.</summary>
    Paired,
}

/// <summary>The top bar's connection state (ADR-0028).</summary>
public enum AgentStatus
{
    Checking,
    Connected,
    NotRunning,
    NotPaired,
    PairAgain,
}

/// <summary>Which sender a send uses.</summary>
public enum SendTarget
{
    Server,
    Agent,
}

/// <summary>
/// The rules that turn the probe and the page's pairing into a status and a default send target, as pure functions (WP31). The
/// default is the agent when the page holds a pairing token and the agent answered as paired; otherwise the server. A choice the
/// user made always wins.
/// </summary>
public static class AgentStatusRules
{
    public static AgentStatus Derive(AgentProbe probe, bool pagePaired) => probe switch
    {
        AgentProbe.Unchecked => AgentStatus.Checking,
        AgentProbe.Unreachable => AgentStatus.NotRunning,
        AgentProbe.Refused => AgentStatus.NotPaired,
        AgentProbe.NotPaired => AgentStatus.NotPaired,
        // An agent that is paired but whose token this page does not hold needs a pairing from this page: "Pair again".
        AgentProbe.Paired => pagePaired ? AgentStatus.Connected : AgentStatus.PairAgain,
        _ => AgentStatus.NotRunning,
    };

    public static SendTarget EffectiveTarget(SendTarget? choice, AgentProbe probe, bool pagePaired) =>
        choice ?? (pagePaired && probe == AgentProbe.Paired ? SendTarget.Agent : SendTarget.Server);

    /// <summary>The label of each state, as the top bar shows it.</summary>
    public static string Label(AgentStatus status) => status switch
    {
        AgentStatus.Checking => "Checking…",
        AgentStatus.Connected => "Connected",
        AgentStatus.NotRunning => "Not running",
        AgentStatus.NotPaired => "Not paired",
        AgentStatus.PairAgain => "Pair again",
        _ => "Not running",
    };

    /// <summary>The state name the page exposes in <c>data-state</c>, for tests and styles.</summary>
    public static string Key(AgentStatus status) => status switch
    {
        AgentStatus.Checking => "checking",
        AgentStatus.Connected => "connected",
        AgentStatus.NotRunning => "not-running",
        AgentStatus.NotPaired => "not-paired",
        AgentStatus.PairAgain => "pair-again",
        _ => "not-running",
    };
}
