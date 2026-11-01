using System.Net;
using System.Net.Sockets;

namespace PostmanClone.Sending.Ssrf;

/// <summary>Connects a TCP socket to the endpoint it is given and returns the stream that owns it.</summary>
public sealed class SocketAddressDialer : IAddressDialer
{
    public async Task<Stream> ConnectAsync(IPEndPoint endpoint, CancellationToken cancellationToken)
    {
        var socket = new Socket(endpoint.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
        try
        {
            await socket.ConnectAsync(endpoint, cancellationToken);
        }
        catch
        {
            socket.Dispose();
            throw;
        }

        return new NetworkStream(socket, ownsSocket: true);
    }
}
