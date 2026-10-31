using System.Net.Http;

namespace PostmanClone.ServiceDefaults.Telemetry;

/// <summary>
/// Marks an outbound request whose target was chosen by a user (the sender's requests). The HTTP client instrumentation
/// would record its URL, and a user's path can carry a secret that a variable filled in, so such a request gets no span.
/// The marker travels with the request, so the filter decides per request and no other client is affected (P15).
/// </summary>
public static class OutboundTelemetry
{
    public static readonly HttpRequestOptionsKey<bool> UserTarget = new("PostmanClone.UserTarget");

    /// <summary>True when the request should be traced. A request marked as a user target is not.</summary>
    public static bool RecordsSpan(HttpRequestMessage request) =>
        !(request.Options.TryGetValue(UserTarget, out var userTarget) && userTarget);
}
