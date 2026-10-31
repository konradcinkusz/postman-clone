using PostmanClone.ExecutionService.Sending.Assembly;

namespace PostmanClone.ExecutionService.Api;

/// <summary>The body of <c>POST /api/v1/execute</c> (CONTRACTS §4).</summary>
/// <param name="Request">The saved or edited request, with <c>{{name}}</c> references.</param>
/// <param name="EnvironmentId">The environment whose variables fill the references. Absent: no variables.</param>
/// <param name="SavedRequestId">The saved request this came from, recorded with the history entry.</param>
/// <param name="TimeoutSeconds">1 to the configured maximum; the default is the configured default.</param>
/// <param name="FollowRedirects">Follow redirects (at most the configured maximum). The default is true.</param>
public sealed record ExecuteRequest(
    RequestDefinition Request,
    Guid? EnvironmentId,
    Guid? SavedRequestId,
    int? TimeoutSeconds,
    bool? FollowRedirects);

/// <summary>
/// How a send ended. <see cref="Completed"/> covers any response, including a 404 and a truncated body; the other values
/// name a refusal or a failure (CONTRACTS §4).
/// </summary>
public enum ExecutionOutcome
{
    Completed,
    Blocked,
    Timeout,
    NetworkError,
    InvalidRequest,
    TooLarge,
}

/// <summary>Milliseconds. An unknown phase is 0.</summary>
public sealed record ExecutionTiming(int TotalMs, int DnsMs, int ConnectMs, int TtfbMs, int DownloadMs);

/// <summary>One followed redirect: the status of the response that redirected, and its Location with secrets masked.</summary>
public sealed record RedirectHop(int StatusCode, string Location);

/// <summary>
/// The response body. <c>Encoding</c> is <c>utf8</c> for a text type whose bytes are valid UTF-8, otherwise <c>base64</c>.
/// <c>SizeBytes</c> counts the bytes kept after decoding, so it is at most the read cap.
/// </summary>
public sealed record ResponseBodyDto(string Encoding, string Content, long SizeBytes, bool Truncated, string? ContentType);

public sealed record ResponseDto(
    int StatusCode,
    string ReasonPhrase,
    string HttpVersion,
    IReadOnlyList<HeaderLine> Headers,
    ResponseBodyDto Body);

/// <summary>The answer of <c>POST /api/v1/execute</c>. Secrets are masked in every string field (CONTRACTS §4).</summary>
public sealed record ExecutionResult(
    ExecutionOutcome Outcome,
    string? ErrorCode,
    string? Message,
    ResponseDto? Response,
    ExecutionTiming Timing,
    string FinalUrl,
    IReadOnlyList<RedirectHop> Redirects,
    IReadOnlyList<string> UnresolvedVariables,
    Guid? HistoryId);
