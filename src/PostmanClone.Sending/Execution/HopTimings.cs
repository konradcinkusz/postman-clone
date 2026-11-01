using System.Diagnostics;
using System.Net;
using PostmanClone.Sending.Ssrf;

namespace PostmanClone.Sending.Execution;

/// <summary>
/// Adds up the time spent resolving and connecting for one send, over every hop of a redirect chain. The stopwatch
/// hooks wrap the guard's own resolver and dialer, so the measured time is the guard's time and nothing else.
/// </summary>
internal sealed class HopTimings
{
    private long _dnsTicks;
    private long _connectTicks;

    public int DnsMs => ToMilliseconds(Interlocked.Read(ref _dnsTicks));

    public int ConnectMs => ToMilliseconds(Interlocked.Read(ref _connectTicks));

    public void AddDns(TimeSpan elapsed) => Interlocked.Add(ref _dnsTicks, elapsed.Ticks);

    public void AddConnect(TimeSpan elapsed) => Interlocked.Add(ref _connectTicks, elapsed.Ticks);

    private static int ToMilliseconds(long ticks) => (int)TimeSpan.FromTicks(ticks).TotalMilliseconds;
}

internal sealed class TimedHostResolver(IHostResolver inner, HopTimings timings) : IHostResolver
{
    public async Task<IReadOnlyList<System.Net.IPAddress>> ResolveAsync(string host, CancellationToken cancellationToken)
    {
        var started = Stopwatch.GetTimestamp();
        try
        {
            return await inner.ResolveAsync(host, cancellationToken);
        }
        finally
        {
            timings.AddDns(Stopwatch.GetElapsedTime(started));
        }
    }
}

internal sealed class TimedDialer(IAddressDialer inner, HopTimings timings) : IAddressDialer
{
    public async Task<Stream> ConnectAsync(IPEndPoint endpoint, CancellationToken cancellationToken)
    {
        var started = Stopwatch.GetTimestamp();
        try
        {
            return await inner.ConnectAsync(endpoint, cancellationToken);
        }
        finally
        {
            timings.AddConnect(Stopwatch.GetElapsedTime(started));
        }
    }
}
