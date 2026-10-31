using System.Net;
using System.Net.Sockets;
using PostmanClone.ExecutionService.Options;

namespace PostmanClone.ExecutionService.Sending.Ssrf;

/// <summary>
/// Decides whether the sender may connect to an address (ADR-0011). Loopback and private ranges are lifted only by
/// the matching <see cref="SsrfOptions"/> flag, which the service refuses outside Development. Everything else in the block
/// list is refused unconditionally. An IPv4 address carried inside an IPv6 address (mapped, compatible, NAT64, 6to4)
/// is judged on the IPv4 address it carries, so the wrapper cannot change the answer.
/// </summary>
public sealed class IpRangePolicy(SsrfOptions options)
{
    private enum Relaxation
    {
        None,
        Loopback,
        Private,
    }

    private readonly record struct Block(byte[] Network, int PrefixLength, string Category, Relaxation Relaxation);

    private static readonly Block[] Ipv4Blocks =
    [
        Rule("0.0.0.0", 8, "this_network"),
        Rule("10.0.0.0", 8, "private", Relaxation.Private),
        Rule("100.64.0.0", 10, "shared_address"),
        Rule("127.0.0.0", 8, "loopback", Relaxation.Loopback),
        Rule("169.254.0.0", 16, "link_local", Relaxation.Private),
        Rule("172.16.0.0", 12, "private", Relaxation.Private),
        Rule("192.0.0.0", 24, "reserved"),
        Rule("192.0.2.0", 24, "documentation"),
        Rule("192.168.0.0", 16, "private", Relaxation.Private),
        Rule("192.88.99.0", 24, "reserved"),
        Rule("198.18.0.0", 15, "benchmarking"),
        Rule("198.51.100.0", 24, "documentation"),
        Rule("203.0.113.0", 24, "documentation"),
        Rule("224.0.0.0", 4, "multicast"),
        Rule("255.255.255.255", 32, "broadcast"),
        Rule("240.0.0.0", 4, "reserved"),
    ];

    private static readonly Block[] Ipv6Blocks =
    [
        Rule("::", 128, "unspecified"),
        Rule("::1", 128, "loopback", Relaxation.Loopback),
        Rule("fc00::", 7, "private", Relaxation.Private),
        Rule("fe80::", 10, "link_local", Relaxation.Private),
        Rule("ff00::", 8, "multicast"),
        Rule("2001::", 32, "teredo"),
        Rule("2001:db8::", 32, "documentation"),
        Rule("64:ff9b:1::", 48, "nat64_local"),
    ];

    // Wrappers that carry an IPv4 address in their low bits. The embedded address is checked by the IPv4 table.
    private static readonly byte[] MappedNetwork = IPAddress.Parse("::ffff:0:0").GetAddressBytes();
    private static readonly byte[] CompatibleNetwork = IPAddress.Parse("::").GetAddressBytes();
    private static readonly byte[] Nat64Network = IPAddress.Parse("64:ff9b::").GetAddressBytes();
    private static readonly byte[] SixToFourNetwork = IPAddress.Parse("2002::").GetAddressBytes();

    /// <summary>Checks one address. An IPv6 address with a zone id is refused before any range is consulted.</summary>
    public AddressVerdict Evaluate(IPAddress address)
    {
        switch (address.AddressFamily)
        {
            case AddressFamily.InterNetwork:
                return EvaluateIpv4(address.GetAddressBytes());
            case AddressFamily.InterNetworkV6 when address.ScopeId != 0:
                return Blocked("scoped_address");
            case AddressFamily.InterNetworkV6:
                return EvaluateIpv6(address.GetAddressBytes());
            default:
                return Blocked("unsupported_family");
        }
    }

    private AddressVerdict EvaluateIpv4(ReadOnlySpan<byte> address)
    {
        foreach (var block in Ipv4Blocks)
        {
            if (Matches(address, block.Network, block.PrefixLength))
            {
                return Decide(block);
            }
        }

        return Allowed;
    }

    private AddressVerdict EvaluateIpv6(byte[] address)
    {
        ReadOnlySpan<byte> bytes = address;

        foreach (var block in Ipv6Blocks)
        {
            if (Matches(bytes, block.Network, block.PrefixLength))
            {
                return Decide(block);
            }
        }

        if (Matches(bytes, MappedNetwork, 96) || Matches(bytes, CompatibleNetwork, 96) || Matches(bytes, Nat64Network, 96))
        {
            return EvaluateIpv4(bytes[12..]);
        }

        if (Matches(bytes, SixToFourNetwork, 16))
        {
            return EvaluateIpv4(bytes[2..6]);
        }

        return Allowed;
    }

    private AddressVerdict Decide(Block block)
    {
        var relaxed = block.Relaxation switch
        {
            Relaxation.Loopback => options.AllowLoopback,
            Relaxation.Private => options.AllowPrivateRanges,
            _ => false,
        };

        return relaxed ? Allowed : Blocked(block.Category);
    }

    private static bool Matches(ReadOnlySpan<byte> address, ReadOnlySpan<byte> network, int prefixLength)
    {
        var wholeBytes = prefixLength / 8;
        if (!address[..wholeBytes].SequenceEqual(network[..wholeBytes]))
        {
            return false;
        }

        var remainder = prefixLength % 8;
        if (remainder == 0)
        {
            return true;
        }

        var mask = (byte)(0xFF << (8 - remainder));
        return (address[wholeBytes] & mask) == (network[wholeBytes] & mask);
    }

    private static Block Rule(string network, int prefixLength, string category, Relaxation relaxation = Relaxation.None) =>
        new(IPAddress.Parse(network).GetAddressBytes(), prefixLength, category, relaxation);

    private static AddressVerdict Allowed => new(true, null);

    private static AddressVerdict Blocked(string category) => new(false, category);
}
