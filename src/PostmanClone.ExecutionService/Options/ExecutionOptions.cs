namespace PostmanClone.ExecutionService.Options;

/// <summary>
/// The sender's limits, bound from <c>Execution:</c>. The defaults are CONTRACTS §4; a deployment may only
/// tighten them, and <see cref="ExecutionOptionsValidator"/> refuses values that would widen the contract.
/// </summary>
public sealed class ExecutionOptions
{
    public const string SectionName = "Execution";

    /// <summary>Request body, in bytes (1 MB).</summary>
    public int RequestBodyMaxBytes { get; set; } = 1_048_576;

    public int MaxHeaders { get; set; } = 100;

    public int UrlMaxLength { get; set; } = 8192;

    /// <summary>Bytes read from a response before it is cut off and marked truncated (5 MB).</summary>
    public long ResponseReadCapBytes { get; set; } = 5 * 1024 * 1024;

    /// <summary>A declared Content-Length above this is refused without reading the body (50 MB).</summary>
    public long RefuseDeclaredLengthBytes { get; set; } = 50 * 1024 * 1024;

    public int DefaultTimeoutSeconds { get; set; } = 30;

    public int MaxTimeoutSeconds { get; set; } = 60;

    public int MaxRedirects { get; set; } = 5;

    /// <summary>Entries kept per user and workspace; the oldest is removed on insert.</summary>
    public int HistoryRetentionPerUserWorkspace { get; set; } = 200;

    /// <summary>The response body stored with a history entry, in bytes (64 KB).</summary>
    public int HistoryBodyMaxBytes { get; set; } = 64 * 1024;

    public SsrfOptions Ssrf { get; set; } = new();
}

/// <summary>
/// Relaxes the address checks that SSRF protection applies by default. Both settings exist only
/// for the loopback test stack; the service refuses to start with either set in Production.
/// </summary>
public sealed class SsrfOptions
{
    /// <summary>Allows 127.0.0.0/8 and ::1, so tests can call a server they started on loopback.</summary>
    public bool AllowLoopback { get; set; }

    /// <summary>Allows the RFC 1918, link-local and unique-local ranges.</summary>
    public bool AllowPrivateRanges { get; set; }

    public bool Relaxed => AllowLoopback || AllowPrivateRanges;
}
