namespace PostmanClone.Sending.Ssrf;

/// <summary>
/// Relaxes the address checks that SSRF protection applies by default. Both settings exist only
/// for the loopback test stack; the service refuses to start with either set outside Development.
/// </summary>
public sealed class SsrfOptions
{
    /// <summary>Allows 127.0.0.0/8 and ::1, so tests can call a server they started on loopback.</summary>
    public bool AllowLoopback { get; set; }

    /// <summary>Allows the RFC 1918, link-local and unique-local ranges.</summary>
    public bool AllowPrivateRanges { get; set; }

    /// <summary>
    /// Destination ports the sender never connects to, whatever the address: mail, database, cache and search
    /// services that a user's request should not reach through this service.
    /// </summary>
    public int[] BlockedPorts { get; set; } = [22, 25, 110, 143, 465, 587, 993, 995, 3306, 5432, 6379, 9200, 27017];

    public bool Relaxed => AllowLoopback || AllowPrivateRanges;
}
