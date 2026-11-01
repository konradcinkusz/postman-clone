using System.Net;

namespace PostmanClone.Sending.Ssrf;

/// <summary>
/// The destination rules one sender applies. ExecutionService uses the SSRF rules (ADR-0011): blocked ports and the
/// address ranges of <see cref="SsrfOptions"/>. The agent uses the same class with its own listener's port, so a target
/// that is the agent itself is refused (ADR-0027), and with <see cref="SsrfOptions.AllowLoopback"/> and
/// <see cref="SsrfOptions.AllowPrivateRanges"/> set (the default, <c>Agent:Targets:AllowPrivate</c>) it reaches local targets.
/// </summary>
/// <param name="options">The address and port rules.</param>
/// <param name="ownPort">The port this process listens on, or null when it does not listen.</param>
public sealed class OutboundRules
{
    private readonly IpRangePolicy _policy;
    private readonly int? _ownPort;

    public OutboundRules(SsrfOptions options, int? ownPort = null)
    {
        Options = options;
        _ownPort = ownPort;
        _policy = new IpRangePolicy(options);
    }

    public SsrfOptions Options { get; }

    /// <summary>True when the port is one the sender never connects to, whatever the address.</summary>
    public bool IsBlockedPort(int port) => Options.BlockedPorts.Contains(port);

    /// <summary>Checks one literal address against the ranges the options allow.</summary>
    public AddressVerdict CheckAddress(IPAddress address) => _policy.Evaluate(address);

    /// <summary>
    /// True when the target is this process's own listener: the same port, and a loopback name or address. Only a
    /// process that listens has such a target.
    /// </summary>
    public bool IsOwnListener(string canonicalHost, bool isLiteral, IPAddress? address, int port) =>
        _ownPort is { } own
        && port == own
        && (isLiteral ? address is not null && IPAddress.IsLoopback(address) : TargetValidator.IsLocalhost(canonicalHost));
}
