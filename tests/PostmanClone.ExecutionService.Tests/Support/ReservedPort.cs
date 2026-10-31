using System.Net;
using System.Net.Sockets;

namespace PostmanClone.ExecutionService.Tests.Support;

/// <summary>
/// A loopback port that refuses connections and that nothing else can take. The socket is bound but never listened on, so
/// the kernel answers a connect with a refusal, and the test holds the port until it is disposed. Freeing a port that a
/// listener used (and then connecting to it) races with every other process on the machine that binds ephemeral ports:
/// the port can be reused in between, and the request reaches a live listener (a flaky "refused" test).
/// </summary>
public sealed class ReservedPort : IDisposable
{
    private readonly Socket _socket;

    public ReservedPort()
    {
        _socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        _socket.Bind(new IPEndPoint(IPAddress.Loopback, 0));
    }

    public int Port => ((IPEndPoint)_socket.LocalEndPoint!).Port;

    public Uri Address => new($"http://127.0.0.1:{Port}/");

    public void Dispose() => _socket.Dispose();
}
