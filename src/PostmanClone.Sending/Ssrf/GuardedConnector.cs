using System.Net;
using System.Net.Sockets;
using System.Runtime.ExceptionServices;

namespace PostmanClone.Sending.Ssrf;

/// <summary>
/// The connection-time check, and the only one that is authoritative (ADR-0011). It runs for every new connection,
/// with the endpoint the HTTP stack is about to use. It resolves the name itself, once, and refuses the whole connection
/// if any answer is in a blocked range, so a mixed public and private answer is refused. It then dials only the validated
/// addresses, in the order they were returned. A second resolution, which could answer differently, is never used.
/// </summary>
public sealed class GuardedConnector(SsrfOptions options, IHostResolver resolver, IAddressDialer dialer)
{
    private readonly IpRangePolicy _policy = new(options);

    public async ValueTask<Stream> ConnectAsync(DnsEndPoint endpoint, CancellationToken cancellationToken)
    {
        if (options.BlockedPorts.Contains(endpoint.Port))
        {
            throw new SsrfRejectedException(SsrfErrorCodes.BlockedPort);
        }

        var addresses = await ResolveAsync(endpoint.Host, cancellationToken);
        foreach (var address in addresses)
        {
            var verdict = _policy.Evaluate(address);
            if (!verdict.Allowed)
            {
                throw new SsrfRejectedException(SsrfErrorCodes.BlockedAddress, verdict.Category);
            }
        }

        Exception? lastFailure = null;
        foreach (var address in addresses)
        {
            try
            {
                return await dialer.ConnectAsync(new IPEndPoint(address, endpoint.Port), cancellationToken);
            }
            catch (SocketException failure)
            {
                lastFailure = failure;
            }
        }

        ExceptionDispatchInfo.Capture(lastFailure!).Throw();
        throw new InvalidOperationException("Unreachable: a validated address list is never empty.");
    }

    private async Task<IReadOnlyList<IPAddress>> ResolveAsync(string host, CancellationToken cancellationToken)
    {
        switch (IpLiteralParser.Classify(host, out var literal))
        {
            case HostKind.Address:
                return [literal!];
            case HostKind.Invalid:
                throw new SsrfRejectedException(SsrfErrorCodes.InvalidUrl);
        }

        IReadOnlyList<IPAddress> answers;
        try
        {
            answers = await resolver.ResolveAsync(host, cancellationToken);
        }
        catch (SocketException)
        {
            throw new SsrfRejectedException(SsrfErrorCodes.DnsFailed);
        }

        if (answers.Count == 0)
        {
            throw new SsrfRejectedException(SsrfErrorCodes.DnsFailed);
        }

        return answers;
    }
}
