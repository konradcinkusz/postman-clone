using PostmanClone.ExecutionService.Sending.Ssrf;
using Xunit;

namespace PostmanClone.ExecutionService.Tests.Sending.Ssrf;

/// <summary>Every spelling of an IP literal reads as the address it denotes, and every malformed one is refused.</summary>
public sealed class IpLiteralParserTests
{
    [Theory]
    // decimal, octal and hex spellings of 127.0.0.1
    [InlineData("2130706433", "127.0.0.1")]
    [InlineData("0177.0.0.1", "127.0.0.1")]
    [InlineData("0x7f.1", "127.0.0.1")]
    [InlineData("0x7f000001", "127.0.0.1")]
    [InlineData("017700000001", "127.0.0.1")]
    [InlineData("0X7F.0X0.0.1", "127.0.0.1")]
    [InlineData("0x7f.0.0.0x1", "127.0.0.1")]
    [InlineData("127.1", "127.0.0.1")]
    [InlineData("127.0.1", "127.0.0.1")]
    // every part in octal, and every part in hex, of 169.254.169.254
    [InlineData("0251.0376.0251.0376", "169.254.169.254")]
    [InlineData("0xa9.0xfe.0xa9.0xfe", "169.254.169.254")]
    // the widest values and the zero forms
    [InlineData("0", "0.0.0.0")]
    [InlineData("0x0", "0.0.0.0")]
    [InlineData("0x", "0.0.0.0")]
    [InlineData("4294967295", "255.255.255.255")]
    [InlineData("0xffffffff", "255.255.255.255")]
    [InlineData("0xFFFFFFFF", "255.255.255.255")]
    // a trailing dot is part of the spelling, not a new part
    [InlineData("127.0.0.1.", "127.0.0.1")]
    [InlineData("1.2.3", "1.2.0.3")]
    [InlineData("1.2", "1.0.0.2")]
    [InlineData("10.0.0.1", "10.0.0.1")]
    [InlineData("93.184.216.34", "93.184.216.34")]
    // IPv6, bracketed or bare, and the IPv4-mapped form in its hex spelling
    [InlineData("[::1]", "::1")]
    [InlineData("::1", "::1")]
    [InlineData("[::ffff:127.0.0.1]", "::ffff:127.0.0.1")]
    [InlineData("[0:0:0:0:0:ffff:7f00:1]", "::ffff:127.0.0.1")]
    [InlineData("[2606:4700:4700::1111]", "2606:4700:4700::1111")]
    public void A_literal_spelling_reads_as_the_address_it_denotes(string text, string expected)
    {
        var kind = IpLiteralParser.Classify(text, out var address);

        Assert.Equal(HostKind.Address, kind);
        Assert.Equal(expected, address!.ToString());
    }

    [Theory]
    [InlineData("4294967296")]
    [InlineData("0x100000000")]
    [InlineData("256.0.0.1")]
    [InlineData("1.2.3.256")]
    [InlineData("1.2.65536")]
    [InlineData("1.2.3.4.5")]
    [InlineData("08.1.1.1")]
    [InlineData("0x1g.0.0.1")]
    [InlineData("1..2.3")]
    public void A_number_that_is_not_a_valid_address_is_invalid_and_never_a_name(string text)
    {
        var kind = IpLiteralParser.Classify(text, out var address);

        Assert.Equal(HostKind.Invalid, kind);
        Assert.Null(address);
    }

    [Theory]
    [InlineData("example.com")]
    [InlineData("localhost")]
    [InlineData("a.b.c.d")]
    [InlineData("1.2.3.abc")]
    [InlineData("0x7f.example")]
    [InlineData("xn--bcher-kva.example")]
    public void A_name_is_not_an_address(string text)
    {
        var kind = IpLiteralParser.Classify(text, out var address);

        Assert.Equal(HostKind.Name, kind);
        Assert.Null(address);
    }

    [Theory]
    [InlineData("[::1")]
    [InlineData("[::1]x")]
    [InlineData("[fe80::1%25eth0]")]
    [InlineData("[]")]
    [InlineData("::1%1")]
    [InlineData("[")]
    [InlineData("")]
    public void A_malformed_or_zone_scoped_IPv6_literal_is_invalid(string text)
    {
        var kind = IpLiteralParser.Classify(text, out var address);

        Assert.Equal(HostKind.Invalid, kind);
        Assert.Null(address);
    }
}
