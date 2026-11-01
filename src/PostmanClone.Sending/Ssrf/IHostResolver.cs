using System.Net;

namespace PostmanClone.Sending.Ssrf;

/// <summary>Resolves a host name to addresses. The guard calls it once per connection and checks every answer.</summary>
public interface IHostResolver
{
    Task<IReadOnlyList<IPAddress>> ResolveAsync(string host, CancellationToken cancellationToken);
}
