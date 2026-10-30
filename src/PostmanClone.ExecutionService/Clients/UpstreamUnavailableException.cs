namespace PostmanClone.ExecutionService.Clients;

/// <summary>
/// The EnvironmentsService answered with something other than the contract (a 3xx, an unexpected status, a body
/// that does not parse), or did not answer in time. The endpoints map it to <c>502 upstream_unavailable</c>.
/// </summary>
public sealed class UpstreamUnavailableException(string message, Exception? inner = null) : Exception(message, inner);
