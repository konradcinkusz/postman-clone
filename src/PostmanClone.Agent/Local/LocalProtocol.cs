using System.Net.Http.Headers;

namespace PostmanClone.Agent.Local;

/// <summary>
/// The rules of the local listener (CONTRACTS A2.6, ADR-0026 Decision 5), as pure functions so each can be tested without a server.
/// Every request is checked for its <c>Host</c> and <c>Origin</c> before anything else runs.
/// </summary>
public static class LocalProtocol
{
    public const int DefaultPort = 47631;

    /// <summary>The largest body the local listener reads (CONTRACTS A2.6: 2 MB).</summary>
    public const long MaxBodyBytes = 2 * 1024 * 1024;

    public const string AllowMethods = "GET, POST, OPTIONS";

    public const string AllowHeaders = "Content-Type, X-Agent-Pairing-Token";

    public const string MaxAge = "600";

    /// <summary>The <c>Host</c> must name the loopback address or <c>localhost</c>, with the agent's port (DNS rebinding, ADR-0026).</summary>
    public static bool HostAllowed(string? host, int port) =>
        host is not null
        && (string.Equals(host, $"127.0.0.1:{port}", StringComparison.Ordinal)
            || string.Equals(host, $"localhost:{port}", StringComparison.OrdinalIgnoreCase));

    /// <summary>The <c>Origin</c> must be present and equal one allowed origin exactly (scheme, host and port). Absent is refused.</summary>
    public static bool OriginAllowed(string? origin, IReadOnlyList<string> allowedOrigins) =>
        !string.IsNullOrEmpty(origin)
        && allowedOrigins.Any(allowed => string.Equals(allowed, origin, StringComparison.OrdinalIgnoreCase));

    /// <summary>True for <c>application/json</c>, with or without parameters. Any other type is refused with 415.</summary>
    public static bool IsJson(string? contentType) =>
        MediaTypeHeaderValue.TryParse(contentType, out var parsed)
        && string.Equals(parsed.MediaType, "application/json", StringComparison.OrdinalIgnoreCase);

    /// <summary>The pairing code's shape before it is sent to the server: 10 to 20 characters (CONTRACTS A2.3).</summary>
    public static bool PairingCodeShapeValid(string? code) => code is { Length: >= 10 and <= 20 };
}
