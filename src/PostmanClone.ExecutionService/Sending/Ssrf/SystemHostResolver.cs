using System.Net;

namespace PostmanClone.ExecutionService.Sending.Ssrf;

/// <summary>Resolves names with the operating system's resolver. It is the production resolver; tests substitute their own.</summary>
public sealed class SystemHostResolver : IHostResolver
{
    public async Task<IReadOnlyList<IPAddress>> ResolveAsync(string host, CancellationToken cancellationToken) =>
        await Dns.GetHostAddressesAsync(host, cancellationToken);
}
