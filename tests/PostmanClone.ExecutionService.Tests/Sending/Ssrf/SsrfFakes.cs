using System.Net;
using System.Net.Sockets;
using PostmanClone.ExecutionService.Sending.Ssrf;

namespace PostmanClone.ExecutionService.Tests.Sending.Ssrf;

/// <summary>
/// A resolver whose answers are scripted per call: call n gets answer n, and the last answer repeats. It counts the calls,
/// so a test can show the name was resolved once per connection and never a second time.
/// </summary>
internal sealed class ScriptedResolver(params Func<IReadOnlyList<IPAddress>>[] answers) : IHostResolver
{
    public int Calls { get; private set; }

    public Task<IReadOnlyList<IPAddress>> ResolveAsync(string host, CancellationToken cancellationToken)
    {
        var answer = answers[Math.Min(Calls, answers.Length - 1)];
        Calls++;
        return Task.FromResult(answer());
    }

    /// <summary>An answer of the given addresses.</summary>
    public static Func<IReadOnlyList<IPAddress>> Answer(params string[] addresses) =>
        () => addresses.Select(IPAddress.Parse).ToList();

    /// <summary>An answer that fails the way the operating system's resolver fails.</summary>
    public static Func<IReadOnlyList<IPAddress>> Failure() =>
        () => throw new SocketException((int)SocketError.HostNotFound);
}

/// <summary>
/// A dialer that records every endpoint it is asked for. <paramref name="refuse"/> decides which endpoints fail with a
/// socket error, so a test can show the guard moving on to the next validated address and never to an unvalidated one.
/// </summary>
internal sealed class RecordingDialer(Func<IPEndPoint, bool>? refuse = null) : IAddressDialer
{
    public List<IPEndPoint> Dialled { get; } = [];

    public Task<Stream> ConnectAsync(IPEndPoint endpoint, CancellationToken cancellationToken)
    {
        Dialled.Add(endpoint);
        if (refuse?.Invoke(endpoint) == true)
        {
            throw new SocketException((int)SocketError.ConnectionRefused);
        }

        return Task.FromResult<Stream>(new MemoryStream());
    }
}
