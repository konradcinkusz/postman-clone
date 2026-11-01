using Microsoft.Extensions.Configuration;

namespace PostmanClone.Web.Agent;

/// <summary>
/// Where the page may reach the local agent (CONTRACTS A2.6, ADR-0028): the port and the origins from <c>Agent:Port</c> and
/// <c>Agent:ConnectOrigins</c>. Each origin must be the loopback address (<c>127.0.0.1</c> or <c>localhost</c>), over <c>http</c>,
/// on that port and nothing else. Anything else fails at startup, so the page's <c>connect-src</c> never gains a wildcard, a
/// public host or a second port.
/// </summary>
public sealed class AgentWebOptions
{
    public const string SectionName = "Agent";

    /// <summary>The agent's listening port (CONTRACTS A2.6).</summary>
    public const int DefaultPort = 47631;

    public int Port { get; private init; } = DefaultPort;

    /// <summary>The origins the page may connect to, in the order the page probes them. Also the <c>connect-src</c> additions.</summary>
    public IReadOnlyList<string> ConnectOrigins { get; private init; } = DefaultOrigins(DefaultPort);

    /// <summary>Reads the section. A missing section gives the defaults; a malformed one stops the host before it serves anything.</summary>
    public static AgentWebOptions From(IConfiguration configuration)
    {
        var section = configuration.GetSection(SectionName);

        var port = DefaultPort;
        if (section["Port"] is { Length: > 0 } text)
        {
            if (!int.TryParse(text, out port) || port is < 1 or > 65535)
            {
                throw new InvalidOperationException("Agent:Port must be a port number from 1 to 65535.");
            }
        }

        var configured = section.GetSection("ConnectOrigins").Get<string[]>();
        var origins = configured is { Length: > 0 } ? configured : DefaultOrigins(port);
        foreach (var origin in origins)
        {
            if (!IsLoopbackOrigin(origin, port))
            {
                throw new InvalidOperationException(
                    $"Agent:ConnectOrigins names '{origin}', which is not the agent's loopback origin on port {port}. Only http://127.0.0.1:{port} and http://localhost:{port} are allowed.");
            }
        }

        return new AgentWebOptions
        {
            Port = port,
            ConnectOrigins = origins.Distinct(StringComparer.Ordinal).ToArray(),
        };
    }

    /// <summary>The two loopback origins of the port: the IP first, then the name (the agent listens on the IP only).</summary>
    public static IReadOnlyList<string> DefaultOrigins(int port) => [$"http://127.0.0.1:{port}", $"http://localhost:{port}"];

    /// <summary>True only for an exact <c>http://127.0.0.1:port</c> or <c>http://localhost:port</c>: no path, no user info, no wildcard.</summary>
    public static bool IsLoopbackOrigin(string? origin, int port)
    {
        if (string.IsNullOrEmpty(origin) || !Uri.TryCreate(origin, UriKind.Absolute, out var uri))
        {
            return false;
        }

        return uri.Scheme == Uri.UriSchemeHttp
            && (uri.Host == "127.0.0.1" || uri.Host == "localhost")
            && uri.Port == port
            && string.Equals(origin, $"http://{uri.Host}:{port}", StringComparison.Ordinal);
    }
}
