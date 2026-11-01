using System.Net;

namespace PostmanClone.Sending.Ssrf;

/// <summary>Opens a TCP connection to one endpoint. The guard only ever passes it an address it has validated.</summary>
public interface IAddressDialer
{
    Task<Stream> ConnectAsync(IPEndPoint endpoint, CancellationToken cancellationToken);
}
