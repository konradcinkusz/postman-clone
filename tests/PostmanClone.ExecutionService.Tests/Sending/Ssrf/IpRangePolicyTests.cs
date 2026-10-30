using System.Net;
using PostmanClone.ExecutionService.Options;
using PostmanClone.ExecutionService.Sending.Ssrf;
using Xunit;

namespace PostmanClone.ExecutionService.Tests.Sending.Ssrf;

/// <summary>Every block edge (first and last address, and one either side), the embedded-IPv4 wrappers, and the flags.</summary>
public sealed class IpRangePolicyTests
{
    [Theory]
    // this_network 0.0.0.0/8
    [InlineData("0.0.0.0", false, "this_network")]
    [InlineData("0.255.255.255", false, "this_network")]
    [InlineData("1.0.0.0", true, null)]
    // private 10.0.0.0/8
    [InlineData("9.255.255.255", true, null)]
    [InlineData("10.0.0.0", false, "private")]
    [InlineData("10.255.255.255", false, "private")]
    [InlineData("11.0.0.0", true, null)]
    // shared address space 100.64.0.0/10
    [InlineData("100.63.255.255", true, null)]
    [InlineData("100.64.0.0", false, "shared_address")]
    [InlineData("100.127.255.255", false, "shared_address")]
    [InlineData("100.128.0.0", true, null)]
    // loopback 127.0.0.0/8
    [InlineData("126.255.255.255", true, null)]
    [InlineData("127.0.0.0", false, "loopback")]
    [InlineData("127.255.255.255", false, "loopback")]
    [InlineData("128.0.0.0", true, null)]
    // link-local 169.254.0.0/16, the cloud metadata address
    [InlineData("169.253.255.255", true, null)]
    [InlineData("169.254.0.0", false, "link_local")]
    [InlineData("169.254.169.254", false, "link_local")]
    [InlineData("169.254.255.255", false, "link_local")]
    [InlineData("169.255.0.0", true, null)]
    // private 172.16.0.0/12
    [InlineData("172.15.255.255", true, null)]
    [InlineData("172.16.0.0", false, "private")]
    [InlineData("172.31.255.255", false, "private")]
    [InlineData("172.32.0.0", true, null)]
    [InlineData("191.255.255.255", true, null)]
    // IETF protocol assignments 192.0.0.0/24
    [InlineData("192.0.0.0", false, "reserved")]
    [InlineData("192.0.0.255", false, "reserved")]
    [InlineData("192.0.1.0", true, null)]
    // documentation 192.0.2.0/24
    [InlineData("192.0.2.0", false, "documentation")]
    [InlineData("192.0.2.255", false, "documentation")]
    [InlineData("192.0.3.0", true, null)]
    // private 192.168.0.0/16
    [InlineData("192.167.255.255", true, null)]
    [InlineData("192.168.0.0", false, "private")]
    [InlineData("192.168.255.255", false, "private")]
    [InlineData("192.169.0.0", true, null)]
    // benchmarking 198.18.0.0/15
    [InlineData("198.17.255.255", true, null)]
    [InlineData("198.18.0.0", false, "benchmarking")]
    [InlineData("198.19.255.255", false, "benchmarking")]
    [InlineData("198.20.0.0", true, null)]
    // documentation 198.51.100.0/24
    [InlineData("198.51.99.255", true, null)]
    [InlineData("198.51.100.0", false, "documentation")]
    [InlineData("198.51.100.255", false, "documentation")]
    [InlineData("198.51.101.0", true, null)]
    // documentation 203.0.113.0/24
    [InlineData("203.0.112.255", true, null)]
    [InlineData("203.0.113.0", false, "documentation")]
    [InlineData("203.0.113.255", false, "documentation")]
    [InlineData("203.0.114.0", true, null)]
    // multicast 224.0.0.0/4 and reserved 240.0.0.0/4
    [InlineData("223.255.255.255", true, null)]
    [InlineData("224.0.0.0", false, "multicast")]
    [InlineData("239.255.255.255", false, "multicast")]
    [InlineData("240.0.0.0", false, "reserved")]
    [InlineData("255.255.255.254", false, "reserved")]
    [InlineData("255.255.255.255", false, "broadcast")]
    // ordinary public addresses
    [InlineData("8.8.8.8", true, null)]
    [InlineData("93.184.216.34", true, null)]
    public void IPv4_block_edges_are_judged_by_their_range(string address, bool allowed, string? category)
    {
        var verdict = Evaluate(address);

        Assert.Equal(allowed, verdict.Allowed);
        Assert.Equal(category, verdict.Category);
    }

    [Theory]
    [InlineData("::", false, "unspecified")]
    [InlineData("::1", false, "loopback")]
    // IPv4-compatible (deprecated) addresses are judged on the IPv4 address they carry
    [InlineData("::2", false, "this_network")]
    [InlineData("::127.0.0.1", false, "loopback")]
    [InlineData("::8.8.8.8", true, null)]
    // link-local fe80::/10, unique-local fc00::/7, multicast ff00::/8, documentation 2001:db8::/32
    [InlineData("fe80::1", false, "link_local")]
    [InlineData("febf:ffff:ffff:ffff:ffff:ffff:ffff:ffff", false, "link_local")]
    [InlineData("fec0::1", true, null)]
    [InlineData("fbff::", true, null)]
    [InlineData("fc00::", false, "private")]
    [InlineData("fdff:ffff::1", false, "private")]
    [InlineData("fe00::", true, null)]
    [InlineData("ff02::1", false, "multicast")]
    [InlineData("ff00::", false, "multicast")]
    [InlineData("2001:db8::", false, "documentation")]
    [InlineData("2001:db8:ffff:ffff:ffff:ffff:ffff:ffff", false, "documentation")]
    [InlineData("2001:db7:ffff::", true, null)]
    [InlineData("2001:db9::", true, null)]
    [InlineData("2606:4700:4700::1111", true, null)]
    [InlineData("2001:4860:4860::8888", true, null)]
    // IPv4-mapped ::ffff:0:0/96
    [InlineData("::ffff:127.0.0.1", false, "loopback")]
    [InlineData("::ffff:10.0.0.1", false, "private")]
    [InlineData("::ffff:169.254.169.254", false, "link_local")]
    [InlineData("::ffff:100.64.0.1", false, "shared_address")]
    [InlineData("::ffff:0.0.0.0", false, "this_network")]
    [InlineData("::ffff:224.0.0.1", false, "multicast")]
    [InlineData("::ffff:8.8.8.8", true, null)]
    [InlineData("::ffff:93.184.216.34", true, null)]
    // NAT64 64:ff9b::/96 (RFC 6052)
    [InlineData("64:ff9b::7f00:1", false, "loopback")]
    [InlineData("64:ff9b::a9fe:a9fe", false, "link_local")]
    [InlineData("64:ff9b::808:808", true, null)]
    // local-use NAT64 64:ff9b:1::/48 is refused whatever it embeds
    [InlineData("64:ff9b:1::1", false, "nat64_local")]
    [InlineData("64:ff9b:1::808:808", false, "nat64_local")]
    // 6to4 2002::/16 carries the IPv4 address in its second and third groups
    [InlineData("2002:7f00:1::", false, "loopback")]
    [InlineData("2002:0a00:1::", false, "private")]
    [InlineData("2002:c0a8:101::", false, "private")]
    [InlineData("2002:c633:6400::", false, "documentation")]
    [InlineData("2002:808:808::", true, null)]
    public void IPv6_blocks_and_embedded_IPv4_are_judged_by_the_address_they_carry(string address, bool allowed, string? category)
    {
        var verdict = Evaluate(address);

        Assert.Equal(allowed, verdict.Allowed);
        Assert.Equal(category, verdict.Category);
    }

    [Theory]
    [InlineData("fe80::1%2")]
    [InlineData("2606:4700:4700::1111%3")]
    [InlineData("::1%1")]
    public void An_IPv6_address_with_a_zone_id_is_refused_before_any_range(string address)
    {
        var verdict = new IpRangePolicy(new SsrfOptions()).Evaluate(IPAddress.Parse(address));

        Assert.False(verdict.Allowed);
        Assert.Equal("scoped_address", verdict.Category);
    }

    [Theory]
    [InlineData("127.0.0.1", true)]
    [InlineData("127.255.255.255", true)]
    [InlineData("::1", true)]
    [InlineData("::ffff:127.0.0.1", true)]
    [InlineData("10.0.0.1", false)]
    [InlineData("169.254.169.254", false)]
    [InlineData("fc00::1", false)]
    [InlineData("100.64.0.1", false)]
    public void AllowLoopback_lifts_only_the_loopback_range(string address, bool allowed)
    {
        var verdict = Evaluate(address, new SsrfOptions { AllowLoopback = true });

        Assert.Equal(allowed, verdict.Allowed);
    }

    [Theory]
    [InlineData("10.0.0.1", true)]
    [InlineData("172.16.0.1", true)]
    [InlineData("192.168.1.1", true)]
    [InlineData("169.254.169.254", true)]
    [InlineData("fc00::1", true)]
    [InlineData("fe80::1", true)]
    [InlineData("::ffff:10.0.0.1", true)]
    [InlineData("64:ff9b::a00:1", true)]
    [InlineData("2002:0a00:1::", true)]
    [InlineData("127.0.0.1", false)]
    [InlineData("100.64.0.1", false)]
    [InlineData("224.0.0.1", false)]
    [InlineData("2001:db8::1", false)]
    [InlineData("64:ff9b:1::a00:1", false)]
    public void AllowPrivateRanges_lifts_only_the_private_link_local_and_unique_local_ranges(string address, bool allowed)
    {
        var verdict = Evaluate(address, new SsrfOptions { AllowPrivateRanges = true });

        Assert.Equal(allowed, verdict.Allowed);
    }

    [Theory]
    [InlineData("127.0.0.1", true)]
    [InlineData("10.0.0.1", true)]
    [InlineData("100.64.0.1", false)]
    [InlineData("224.0.0.1", false)]
    public void Both_flags_lift_their_own_ranges_and_nothing_else(string address, bool allowed)
    {
        var verdict = Evaluate(address, new SsrfOptions { AllowLoopback = true, AllowPrivateRanges = true });

        Assert.Equal(allowed, verdict.Allowed);
    }

    private static AddressVerdict Evaluate(string address, SsrfOptions? options = null) =>
        new IpRangePolicy(options ?? new SsrfOptions()).Evaluate(IPAddress.Parse(address));
}
