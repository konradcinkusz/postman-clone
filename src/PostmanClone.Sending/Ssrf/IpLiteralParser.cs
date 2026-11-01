using System.Net;
using System.Net.Sockets;

namespace PostmanClone.Sending.Ssrf;

/// <summary>Whether a host is a name, an IP literal, or an IP-looking spelling that is not a valid address.</summary>
public enum HostKind
{
    Name,
    Address,
    Invalid,
}

/// <summary>
/// Reads a host as an IP literal the way a URL parser does, so every spelling of one address is judged as that address:
/// <c>2130706433</c>, <c>0177.0.0.1</c>, <c>0x7f.1</c>, <c>127.1</c> and <c>0x7f000001</c> are all 127.0.0.1. The
/// sender checks the value this produces, never the text, so an odd spelling cannot pass as a name.
/// </summary>
public static class IpLiteralParser
{
    /// <summary>
    /// Classifies a host. A bracketed IPv6 literal, a bare IPv6 literal (what a socket endpoint carries), or an IPv4
    /// spelling yields <see cref="HostKind.Address"/>. Text whose last part is a number but is not a valid address is
    /// <see cref="HostKind.Invalid"/>. Anything else is a <see cref="HostKind.Name"/>.
    /// </summary>
    public static HostKind Classify(string host, out IPAddress? address)
    {
        address = null;
        if (host.Length == 0)
        {
            return HostKind.Invalid;
        }

        if (host[0] == '[')
        {
            var closed = host.Length >= 2 && host[^1] == ']';
            return closed && TryParseIpv6(host[1..^1], out address) ? HostKind.Address : HostKind.Invalid;
        }

        if (host.Contains(':'))
        {
            return TryParseIpv6(host, out address) ? HostKind.Address : HostKind.Invalid;
        }

        return ClassifyIpv4(host, out address);
    }

    private static HostKind ClassifyIpv4(string text, out IPAddress? address)
    {
        address = null;
        var parts = text.Split('.');
        if (parts.Length > 1 && parts[^1].Length == 0)
        {
            parts = parts[..^1];
        }

        if (!EndsInNumber(parts[^1]))
        {
            return HostKind.Name;
        }

        if (parts.Length > 4)
        {
            return HostKind.Invalid;
        }

        var numbers = new ulong[parts.Length];
        for (var i = 0; i < parts.Length; i++)
        {
            if (!TryParseNumber(parts[i], out numbers[i]))
            {
                return HostKind.Invalid;
            }
        }

        var last = parts.Length - 1;
        for (var i = 0; i < last; i++)
        {
            if (numbers[i] > 255)
            {
                return HostKind.Invalid;
            }
        }

        // The last part fills whatever bytes the earlier parts left: 32, 24, 16 or 8 bits.
        if (numbers[last] >= 1UL << (8 * (5 - parts.Length)))
        {
            return HostKind.Invalid;
        }

        var value = numbers[last];
        for (var i = 0; i < last; i++)
        {
            value |= numbers[i] << (8 * (3 - i));
        }

        address = new IPAddress([(byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8), (byte)value]);
        return HostKind.Address;
    }

    private static bool EndsInNumber(string last)
    {
        if (last.Length == 0)
        {
            return false;
        }

        if (last.Length >= 2 && last[0] == '0' && (last[1] is 'x' or 'X'))
        {
            return last[2..].All(char.IsAsciiHexDigit);
        }

        return last.All(char.IsAsciiDigit);
    }

    private static bool TryParseNumber(string part, out ulong value)
    {
        value = 0;
        if (part.Length == 0)
        {
            return false;
        }

        var radix = 10;
        var start = 0;
        if (part.Length >= 2 && part[0] == '0' && (part[1] is 'x' or 'X'))
        {
            radix = 16;
            start = 2;
        }
        else if (part.Length >= 2 && part[0] == '0')
        {
            radix = 8;
            start = 1;
        }

        for (var i = start; i < part.Length; i++)
        {
            var digit = DigitValue(part[i]);
            if (digit < 0 || digit >= radix)
            {
                return false;
            }

            value = (value * (ulong)radix) + (ulong)digit;
            if (value > uint.MaxValue)
            {
                return false;
            }
        }

        return true;
    }

    private static int DigitValue(char c) => c switch
    {
        >= '0' and <= '9' => c - '0',
        >= 'a' and <= 'f' => c - 'a' + 10,
        >= 'A' and <= 'F' => c - 'A' + 10,
        _ => -1,
    };

    private static bool TryParseIpv6(string text, out IPAddress? address)
    {
        address = null;

        // A zone id names an interface, not a destination; it is refused rather than honoured.
        if (text.Contains('%') || !IPAddress.TryParse(text, out var parsed) || parsed.AddressFamily != AddressFamily.InterNetworkV6)
        {
            return false;
        }

        address = parsed;
        return true;
    }
}
