using System.Net;

namespace PostmanClone.Sending.Ssrf;

/// <summary>
/// Builds the one <see cref="SocketsHttpHandler"/> the sender may use for outbound requests. The settings are part of
/// the guard: no ambient proxy (an environment variable must not route a request past the checks), no cookie jar,
/// no automatic redirects (each hop is re-validated by <see cref="RedirectPlanner"/>), and no automatic decompression
/// (the response bytes are reported as received).
/// </summary>
public static class SsrfHandlerFactory
{
    public static SocketsHttpHandler Create(SsrfOptions options, IHostResolver resolver, IAddressDialer dialer)
    {
        var connector = new GuardedConnector(options, resolver, dialer);
        return new SocketsHttpHandler
        {
            UseProxy = false,
            Proxy = null,
            UseCookies = false,
            AllowAutoRedirect = false,
            AutomaticDecompression = DecompressionMethods.None,
            ConnectCallback = (context, cancellationToken) => connector.ConnectAsync(context.DnsEndPoint, cancellationToken),
        };
    }
}
